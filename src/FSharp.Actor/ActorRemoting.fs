namespace FSharp.Actor

open System
open System.Threading
open System.Net.Sockets
open System.Collections.Concurrent

type RemoteMessage = {
    Id : uint64 option
    Target : ActorPath
    Sender : ActorPath
    Message : obj
}

type ITransport =
    inherit IDisposable
    abstract Scheme : string with get
    abstract BasePath : ActorPath with get
    abstract Post : ActorPath * RemoteMessage -> unit
    abstract Start : ISerializer * CancellationToken -> unit

type RemoteActor(path:ActorPath, transport:ITransport) =
    override x.ToString() = path.ToString()

    interface IActor with
        member x.Path with get() = path
        member x.Post(msg) =
            transport.Post(path, { Id = msg.Id; Target = path; Sender = ActorPath.rebase transport.BasePath msg.Sender.Path; Message = msg.Message })
        member x.Dispose() = ()

type RemotingEvents =
    | NewActorHost of string * NetAddress
    | ShutdownActorHost of string * NetAddress

type ActorProtocol = 
    | Resolve of ActorPath
    | Resolved of ActorPath list
    | Error of string * exn

type ActorDiscoveryBeacon =
    | ActorDiscoveryBeacon of hostName:string * NetAddress
    | ActorShutdownBeacon of hostName:string * NetAddress

type ActorRegistryTransportSettings = {
    TransportHandler : ((NetAddress * ActorProtocol * MessageId) -> unit)
    CancellationToken : CancellationToken
    Serializer : ISerializer
}

type IActorRegistryTransport = 
    inherit IDisposable
    abstract ListeningEndpoint : NetAddress with get
    abstract Post : NetAddress * ActorProtocol * MessageId -> unit
    abstract Start : ActorRegistryTransportSettings -> unit

type ActorRegistryDiscoverySettings = {
    SystemDetails : (string * NetAddress)
    DiscoveryHandler : (ActorDiscoveryBeacon -> unit)
    CancellationToken : CancellationToken
    Serializer : ISerializer
}

type IActorRegistryDiscovery =
    inherit IDisposable 
    abstract Start : ActorRegistryDiscoverySettings -> unit

type RemotableInMemoryActorRegistry(transports : seq<ITransport>, registryTransport:IActorRegistryTransport, discovery:IActorRegistryDiscovery, actorHost:ActorHost) = 
    
    let registry = new InMemoryActorRegistry() :> ActorRegistry
    let messages = new ConcurrentDictionary<Guid, AsyncResultCell<ActorProtocol>>()
    let clients = new ConcurrentDictionary<string,NetAddress>()
    let logger = Logger.create "RemoteRegistry"
    let transports = 
        Seq.fold (fun (s:Map<string, ITransport>) (t:ITransport) -> t.Start(actorHost.Serializer, actorHost.CancelToken); s.Add(t.Scheme, t)) Map.empty transports

    let resolveTransport name = 
        transports.TryFind name

    let transportHandler = (fun (address:NetAddress, msg:ActorProtocol, messageId:MessageId) -> 
            try
                match msg with
                | Resolve(path) -> 
                    let actorTransport = 
                        path.Transport |> Option.bind resolveTransport
                    let resolvedPaths = 
                        match actorTransport with
                        | Some(t) -> 
                            registry.Resolve path
                            |> List.map (fun ref -> ref.Path |> ActorPath.rebase t.BasePath)
                        | None ->
                            let tcp = resolveTransport "actor.tcp" |> Option.get
                            registry.Resolve path
                            |> List.map (fun ref -> ref.Path |> ActorPath.rebase tcp.BasePath)
                    registryTransport.Post(address,(Resolved resolvedPaths), messageId)
                | Resolved _ as rs -> 
                    match messages.TryGetValue(messageId) with
                    | true, resultCell -> resultCell.Complete(rs)
                    | false , _ -> ()
                | Error(msg, err) -> 
                    logger.Error(sprintf "Remote error received %s" msg, exn = err)
            with e -> 
               let msg = sprintf "TCP: Unable to handle message : %s" e.Message
               logger.Error(msg, exn = new InvalidMessageException(msg, e))
               registryTransport.Post(address, Error(msg,e), messageId)
    )

    let discoveryHandler = (fun (msg:ActorDiscoveryBeacon) ->
            try
                match msg with
                | ActorDiscoveryBeacon(hostName, netAddress) -> 
                    if not <| clients.ContainsKey(hostName)
                    then 
                        clients.TryAdd(hostName, netAddress) |> ignore
                        actorHost.EventStream.Publish(NewActorHost(hostName, netAddress))
                        logger.Debug(sprintf "New actor Host %s %A" hostName netAddress)
                | ActorShutdownBeacon(hostName, netAddress) ->
                    if clients.ContainsKey(hostName)
                    then 
                        let (success,_) = clients.TryRemove(hostName)
                        logger.Debug(sprintf "Actor host shutdown %s %A" hostName netAddress)
            with e -> 
               logger.Error("UDP: Unable to handle message", exn = new InvalidMessageException(msg, e))
    )

    let dispose() =
        transports |> Map.toSeq |> Seq.iter (fun (_,v) -> v.Dispose())
        registryTransport.Dispose()
        discovery.Dispose()
    
    do
        let cancelToken = actorHost.CancelToken
        cancelToken.Register(fun () -> dispose()) |> ignore
        registryTransport.Start({ TransportHandler = transportHandler; CancellationToken = cancelToken; Serializer = actorHost.Serializer })
        discovery.Start({ SystemDetails = (actorHost.Name, registryTransport.ListeningEndpoint); DiscoveryHandler = discoveryHandler; CancellationToken = cancelToken; Serializer = actorHost.Serializer })
    
    let handledResolveResponse result =
         match result with
         | Some(Resolve _) -> failwith "Unexpected message"
         | Some(Resolved rs) ->                        
             List.choose (fun path -> 
                 path.Transport 
                 |> Option.bind resolveTransport
                 |> Option.map (fun transport -> ActorRef(new RemoteActor(path, transport)))
             ) rs
         | Some(Error(msg, err)) -> raise (new Exception(msg, err))
         | None -> failwith "Resolving Path %A timed out"

    let queryClient timeout path address = 
        async { 
                let msgId = Guid.NewGuid()
                let resultCell = new AsyncResultCell<ActorProtocol>()
                messages.AddOrUpdate(msgId, resultCell, fun _ _ -> resultCell) |> ignore
                do registryTransport.Post(address, Resolve path, msgId)
                let! result = resultCell.AwaitResult(?timeout = timeout)
                messages.TryRemove(msgId) |> ignore
                return handledResolveResponse result
        }

    interface IRegistry<ActorPath, ActorRef> with
        member x.All with get() = registry.All
        
        member x.ResolveAsync(path, timeout) =
             async {
                    match path.Host with
                    | None ->
                        let! remotePaths =
                             clients.Values
                             |> Seq.map (queryClient timeout path)
                             |> Async.Parallel
                        let paths = remotePaths |> Array.toList |> List.concat
                        return paths @ registry.Resolve(path)
                    | Some(host) when host <> actorHost.Name ->
                        match clients.TryGetValue host with
                        | true, address -> 
                            let! resolved = queryClient timeout path address
                            return resolved
                        | false, _ -> return []
                    | _ -> 
                        return registry.Resolve(path)
             }
                 
        member x.Resolve(path) =
            (x :> ActorRegistry).ResolveAsync(path, None) |> Async.RunSynchronously                   
        
        member x.Register(actor) = registry.Register actor
        
        member x.UnRegister actor = registry.UnRegister actor

        member x.Dispose() = dispose()

[<AutoOpen>]
module ActorHostRemotingExtensions = 
    
    open Actor

    type ActorHost with
        
        member x.EnableRemoting(transports, registryTransport, udpConfig) = 
            x.Configure (fun c -> 
                { c with Registry = (new RemotableInMemoryActorRegistry(transports, registryTransport, udpConfig, x)) }
            )
            x