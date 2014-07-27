namespace FSharp.Actor

open System

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

type Beacon =
    | Beacon of hostName:string * NetAddress

type RemotableInMemoryActorRegistry(tcpConfig, udpConfig, system:ActorSystem) = 
    
    let registry = new InMemoryActorRegistry() :> ActorRegistry
    let messages = new ConcurrentDictionary<Guid, AsyncResultCell<ActorProtocol>>()
    let clients = new ConcurrentDictionary<string,NetAddress>()
    let tcpChannel : TCP = new TCP(tcpConfig)
    let udpChannel : UDP = new UDP(udpConfig)
    let logger = Log.Logger("RemoteRegistry", Log.defaultFor Log.Debug)

    let tcpHandler = (fun (address:NetAddress, messageId:TcpMessageId, payload:byte[]) -> 
        async { 
            try
                let msg = system.Serializer.Deserialize payload
                match msg with
                | Resolve(path) -> 
                    let transport = 
                        path.Transport |> Option.bind ActorHost.ResolveTransport
                    let resolvedPaths = 
                        match transport with
                        | Some(t) -> 
                            registry.Resolve path
                            |> List.map (fun ref -> ActorRef.path ref |> ActorPath.rebase t.BasePath)
                        | None ->
                            let tcp = ActorHost.ResolveTransport "actor.tcp" |> Option.get
                            registry.Resolve path
                            |> List.map (fun ref -> ActorRef.path ref |> ActorPath.rebase tcp.BasePath)
                    do! tcpChannel.PublishAsync(address.Endpoint, system.Serializer.Serialize (Resolved resolvedPaths), messageId)
                | Resolved _ as rs -> 
                    match messages.TryGetValue(messageId) with
                    | true, resultCell -> resultCell.Complete(rs)
                    | false , _ -> ()
                | Error(msg, err) -> 
                    logger.Error(sprintf "Remote error received %s" msg, exn = err)
            with e -> 
               let msg = sprintf "TCP: Unable to handle message : %s" e.Message
               logger.Error(msg, exn = new InvalidMessageException(e))
               do! tcpChannel.PublishAsync(address.Endpoint, system.Serializer.Serialize (Error(msg,e)), messageId)
        }
    )

    let udpHandler = (fun (address:NetAddress, payload:byte[]) ->
        async {
            try
                let msg = system.Serializer.Deserialize payload
                match msg with
                | Beacon(hostName, netAddress) -> 
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
        let beaconBytes = system.Serializer.Serialize(Beacon(system.Name, NetAddress(tcpChannel.Endpoint)))
        tcpChannel.Start(tcpHandler, system.CancelToken)
        udpChannel.Start(udpHandler, system.CancelToken)
        udpChannel.Heartbeat(5000, (fun () -> beaconBytes), system.CancelToken)
    
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
                                         do! tcpChannel.PublishAsync(client.Endpoint, system.Serializer.Serialize(Resolve path), msgId)
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
        
        member x.EnableRemoting(tcpConfig, udpConfig) = 
            x.Configure (fun c -> 
                c.Registry <- (new RemotableInMemoryActorRegistry(tcpConfig, udpConfig, x))
            )
            x