namespace FSharp.Actor

open System
open System.Threading
open System.Net.Sockets
open System.Collections.Concurrent

type InvalidMessageException(payload:obj, innerEx:Exception) =
    inherit Exception("Unable to handle msg", innerEx)

    member val Buffer = payload with get

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

type ActorRegistryDiscoverySettings = {
    Beacon : ActorDiscoveryBeacon
    DiscoveryHandler : (ActorDiscoveryBeacon -> Async<unit>)
    CancellationToken : CancellationToken
    Serializer : ISerializer
}

type IActorRegistryDiscovery = 
    abstract Start : ActorRegistryDiscoverySettings -> unit

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
               logger.Error(msg, exn = new InvalidMessageException(msg, e))
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
               logger.Error("UDP: Unable to handle message", exn = new InvalidMessageException(msg, e))  
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
                                         messages.AddOrUpdate(msgId, resultCell, fun _ _ -> resultCell) |> ignore
                                         do! transport.Post(client, Resolve path, msgId)
                                         let! result = resultCell.AwaitResult(?timeout = timeout)
                                         messages.TryRemove(msgId) |> ignore
                                         return handledResolveResponse result
                                      })
                         |> Async.Parallel
                    let paths = remotePaths |> Array.toList |> List.concat
                    return paths @ registry.Resolve(path)     
             }
                 
        member x.Resolve(path) =
            let isFromLocalTransport = 
                ActorHost.Transports 
                |> Seq.exists (fun t -> t.BasePath.Transport = path.Transport || t.BasePath.Host = path.Host)

            if isFromLocalTransport
            then registry.Resolve(path)
            else (x :> ActorRegistry).ResolveAsync(path, None) |> Async.RunSynchronously                   
        
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