namespace FSharp.Actor

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Collections.Concurrent
open FSharp.Actor
open Nessos.FsPickler


type TCPTransport(config:TcpConfig, ?logger) as self = 
    let scheme = "actor.tcp"
    let basePath = ActorPath.ofString (sprintf "%s://%s/" scheme (config.ListenerEndpoint.ToString()))
    let log = defaultArg logger (Log.defaultFor Log.Debug)
    let logger = new Log.Logger(sprintf "actor.tcp://%A" config.ListenerEndpoint, log)
    let mutable serializer = Unchecked.defaultof<ISerializer>

    let handler =(fun (address:NetAddress, msgId, payload) -> 
                  async {
                    try
                        let msg = serializer.Deserialize<RemoteMessage>(payload)
                        (!~msg.Target).Post(msg.Message, new RemoteActor(msg.Sender, self) :> IActor |> ActorRef)
                    with e -> 
                        logger.Error("Error handling message: " + e.Message, exn = e)
                  })

    let tcp = new TCP(config) 
    

    interface ITransport with
        member x.Scheme with get() = scheme
        member x.BasePath with get() = basePath
        member x.Post(target, payload) = Async.Start(tcp.PublishAsync((ActorPath.toNetAddress target).Endpoint, serializer.Serialize payload))
        member x.Start(serialiser, ct) = 
            serializer <- serialiser
            tcp.Start(handler, ct)