namespace FSharp.Actor

open System
open System.Threading
open System.Net.Sockets
open System.Collections.Concurrent
open Nessos.FsPickler

type InvalidMessageException(innerEx:Exception) =
    inherit Exception("Unable to handle msg", innerEx)

type RemotingEvents =
    | NewActorSystem of string * NetAddress

type ActorProtocol = 
    | Resolve of actorPath
    | Resolved of actorPath list
    | Error of string * exn

type ActorDiscoveryBeacon =
    | ActorDiscoveryBeacon of hostName:string * NetAddress

type ActorRegistryTransportSettings = {
    TransportHandler : ((NetAddress * ActorProtocol * MessageId) -> Async<unit>)
    CancellationToken : CancellationToken
    Serializer : ISerializer
}

type IActorRegistryTransport = 
    abstract ListeningEndpoint : NetAddress with get
    abstract Post : NetAddress * ActorProtocol * MessageId -> Async<unit>
    abstract Start : ActorRegistryTransportSettings -> unit

type TcpActorRegistryTransport(config:TcpConfig) = 
    let tcpChannel = new TCP(config)
    let logger = Log.Logger("TcpActorRegistryTransport", Log.defaultFor Log.Debug)
    let mutable settings : ActorRegistryTransportSettings = Unchecked.defaultof<_>

    let handler internalHandler = 
        (fun (address,msgId, payload) ->
             async {
                try
                    let msg = settings.Serializer.Deserialize payload
                    do! internalHandler (address,msg,msgId)
                with e -> 
                    logger.Error("Unable to handle Registry Transport message", exn = e) 
             }
        )

    interface IActorRegistryTransport with
        member x.ListeningEndpoint with get() = NetAddress config.ListenerEndpoint
        member x.Post(NetAddress endpoint, msg, msgId) = 
            tcpChannel.PublishAsync(endpoint, settings.Serializer.Serialize msg, msgId)
        member x.Start(setts) = 
            settings <- setts
            tcpChannel.Start(handler settings.TransportHandler, settings.CancellationToken)
  

type ActorRegistryDiscoverySettings = {
    Beacon : ActorDiscoveryBeacon
    DiscoveryHandler : (ActorDiscoveryBeacon -> Async<unit>)
    CancellationToken : CancellationToken
    Serializer : ISerializer
}

type IActorRegistryDiscovery = 
    abstract Start : ActorRegistryDiscoverySettings -> unit

type UdpActorRegistryDiscovery(udpConfig:UdpConfig, ?broadcastInterval) = 
    let udpChannel = new UDP(udpConfig)
    let logger = Log.Logger("UdpActorRegistryDiscovery", Log.defaultFor Log.Debug)
    let broadcastInterval = defaultArg broadcastInterval 1000
    let mutable settings : ActorRegistryDiscoverySettings = Unchecked.defaultof<_>

    let handler internalHandler = 
        (fun (_, payload) ->
            async {
                try
                    let msg = settings.Serializer.Deserialize<ActorDiscoveryBeacon>(payload)
                    do! internalHandler msg
                with e ->
                    logger.Error("Unable to handle discovery msg.", exn = e)
            }
        )

    interface IActorRegistryDiscovery with
        member x.Start(setts) =
            settings <- setts
            let beaconBytes = settings.Serializer.Serialize(settings.Beacon)
            udpChannel.Start(handler settings.DiscoveryHandler, settings.CancellationToken)
            udpChannel.Heartbeat(broadcastInterval, (fun _ -> beaconBytes), settings.CancellationToken)


type RemotableInMemoryActorRegistry(transport:IActorRegistryTransport, discovery:IActorRegistryDiscovery, system:ActorSystem) = 
    
    let registry = new InMemoryActorRegistry() :> ActorRegistry
    let messages = new ConcurrentDictionary<Guid, AsyncResultCell<ActorProtocol>>()
    let clients = new ConcurrentDictionary<string,NetAddress>()
    let logger = Log.Logger("RemoteRegistry", Log.defaultFor Log.Debug)

    let transportHandler = (fun (address:NetAddress, msg:ActorProtocol, messageId:MessageId) -> 
        async { 
            try
                match msg with
                | Resolve(path) -> 
                    let actorTransport = 
                        path.Transport |> Option.bind ActorHost.ResolveTransport
                    let resolvedPaths = 
                        match actorTransport with
                        | Some(t) -> 
                            registry.Resolve path
                            |> List.map (fun ref -> ActorRef.path ref |> ActorPath.rebase t.BasePath)
                        | None ->
                            let tcp = ActorHost.ResolveTransport "actor.tcp" |> Option.get
                            registry.Resolve path
                            |> List.map (fun ref -> ActorRef.path ref |> ActorPath.rebase tcp.BasePath)
                    do! transport.Post(address,(Resolved resolvedPaths), messageId)
                | Resolved _ as rs -> 
                    match messages.TryGetValue(messageId) with
                    | true, resultCell -> resultCell.Complete(rs)
                    | false , _ -> ()
                | Error(msg, err) -> 
                    logger.Error(sprintf "Remote error received %s" msg, exn = err)
            with e -> 
               let msg = sprintf "TCP: Unable to handle message : %s" e.Message
               logger.Error(msg, exn = new InvalidMessageException(e))
               do! transport.Post(address, Error(msg,e), messageId)
        }
    )

    let discoveryHandler = (fun (msg:ActorDiscoveryBeacon) ->
        async {
            try
                match msg with
                | ActorDiscoveryBeacon(hostName, netAddress) -> 
                    if not <| clients.ContainsKey(hostName)
                    then 
                        clients.TryAdd(hostName, netAddress) |> ignore
                        system.EventStream.Publish(NewActorSystem(hostName, netAddress))
                        logger.Debug(sprintf "New actor Host %s %A" hostName netAddress)
            with e -> 
               logger.Error("UDP: Unable to handle message", exn = new InvalidMessageException(e))  
        }
    )
    
    do
        let beacon = ActorDiscoveryBeacon(system.Name, transport.ListeningEndpoint)
        transport.Start({ TransportHandler = transportHandler; CancellationToken = system.CancelToken; Serializer = system.Serializer })
        discovery.Start({ Beacon = beacon; DiscoveryHandler = discoveryHandler; CancellationToken = system.CancelToken; Serializer = system.Serializer })
    
    let handledResolveResponse result =
         match result with
         | Some(Resolve _) -> failwith "Unexpected message"
         | Some(Resolved rs) ->                        
             List.choose (fun path -> 
                 path.Transport 
                 |> Option.bind ActorHost.ResolveTransport
                 |> Option.map (fun transport -> ActorRef(new RemoteActor(path, transport)))
             ) rs
         | Some(Error(msg, err)) -> raise (new Exception(msg, err))
         | None -> failwith "Resolving Path %A timed out" path

    interface IRegistry<actorPath, actorRef> with
        member x.All with get() = registry.All
        
        member x.ResolveAsync(path, timeout) =
             async {
                    let! remotePaths =
                         clients.Values
                         |> Seq.map (fun client -> async { 
                                         let msgId = Guid.NewGuid()
                                         let resultCell = new AsyncResultCell<ActorProtocol>()
                                         messages.TryAdd(msgId, resultCell) |> ignore
                                         do! transport.Post(client, Resolve path, msgId)
                                         let! result = resultCell.AwaitResult(?timeout = timeout)
                                         messages.TryRemove(msgId) |> ignore
                                         return handledResolveResponse result
                                      })
                         |> Async.Parallel
                    let paths = remotePaths |> Array.toList |> List.concat
                    return paths @ registry.Resolve(path)     

             }
                 
        member x.Resolve(path) = (x :> ActorRegistry).ResolveAsync(path, None) |> Async.RunSynchronously                   
        
        member x.Register(actor) = registry.Register actor
        
        member x.UnRegister actor = registry.UnRegister actor

[<AutoOpen>]
module ActorHostRemotingExtensions = 
    
    open Actor

    type ActorSystem with
        
        member x.EnableRemoting(transport, udpConfig) = 
            x.Configure (fun c -> 
                c.Registry <- (new RemotableInMemoryActorRegistry(transport, udpConfig, x))
            )
            x