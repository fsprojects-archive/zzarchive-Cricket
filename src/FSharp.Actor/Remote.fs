namespace FSharp.Actor

open System
open System.Text
open System.IO
open System.Net
open System.Collections.Generic
open System.Runtime.Serialization
open System.Runtime.Serialization.Formatters.Binary
open System.Threading

type RemoteMessageEnvelope = {
    Target : string
    Message : obj
}

type ITransport = 
    inherit IDisposable
    abstract Scheme : string with get
    abstract Send : ActorPath.T * byte[] -> unit
    abstract Start : ActorRef -> unit

type FractureTransportConfiguration = {
    Port : int option
}

type FractureTransport(config : FractureTransportConfiguration) =  

    let mutable handler = NullActor().Ref()

    let onReceived(body:byte[], _, _) = handler.Post(Received(body))
    let onConnected(endPoint:IPEndPoint) = handler.Post(Connected(endPoint))
    let onDisconnected(endPoint) = handler.Post(Disconnected(endPoint))
    let onSent(body:byte[], endPoint) = handler.Post(Sent(body.Length, endPoint))

    let server = Fracture.TcpServer.Create(onReceived, onConnected, onDisconnected, onSent)
    let clients = new Dictionary<string, Fracture.TcpClient>()

    let getOrRegisterClient (path:ActorPath.T) =
        let ep = path.IpEndpoint
        match clients.TryGetValue(ep.ToString()) with
        | true, client -> client
        | false, _ -> 
            let connWaitHandle = new AutoResetEvent(false)
            let client = new Fracture.TcpClient()
            client.Connected |> Observable.add(fun x -> printfn "Client connected"; connWaitHandle.Set() |> ignore) 
            client.Disconnected |> Observable.add (fun x -> printfn "Client disconnected"; clients.Remove(x.ToString()) |> ignore)
            client.Start(ep)
            if connWaitHandle.WaitOne(10000)
            then 
                clients.Add(ep.ToString(), client)
                client
            else raise(TimeoutException(sprintf "Could not connection to client in given time period"))

    interface ITransport with
        member val Scheme = "actor.fracture"  with get

        member x.Send(path, msg) =
            try
                let client = getOrRegisterClient path
                client.Send(msg, true)
            with e ->
                handler.Post(Error <| (CouldNotPost(path), Some e))
                
        member x.Start(actor:ActorRef) = 
            handler <- actor
            server.Listen(IPAddress.Any, defaultArg config.Port 0)

        member x.Dispose() = 
            clients |> Seq.iter (fun k -> k.Value.Dispose())
            clients.Clear()
            server.Dispose()

module Remoting = 
    
    open FSharp.Actor
    open FSharp.Actor.DSL

    let private supervisor = 
        Actor.supervisor ActorPath.RemotingSupervisor Supervisor.OneForOne []
    
    let registerTransport (serialiser:Serialiser) (transport:ITransport) =
        
        let handler = 
            Actor.spawn (ActorPath.forTransport transport.Scheme) (fun (actor:Actor<RemoteMessage>) ->
                async {
                    let! msg = actor.Receive()
                    match msg with
                    | Connected(ep) -> actor.Log.Debug(sprintf "Endpoint connected %A" ep)
                    | Disconnected(ep) -> actor.Log.Debug(sprintf "Endpoint disconnected %A" ep)
                    | Sent(len, ep) -> actor.Log.Debug(sprintf "Sent %d bytes to endpoint %A" len ep)
                    | Post(path, msg) -> 
                        actor.Log.Debug(sprintf "Sending message to endpoint %A" path)
                        transport.Send(path, serialiser.Serialise { Target = path.AbsoluteUri; Message = msg })
                    | Received(body) ->
                        let payload = serialiser.Deserialise body
                        if payload <> null
                        then
                            let payload = payload :?> RemoteMessageEnvelope
                            let path = ActorPath.create payload.Target
                            actor.Log.Debug(sprintf "Received message for %A" path)
                            !*path.AbsolutePath <-* payload.Message
                    | Error(msg, exn) ->
                        match msg with 
                        | CouldNotPost(remotePath) -> actor.Log.Error(sprintf "Remote path not available %A" remotePath, ?exn = exn) 
                }
            ) |> Actor.supervisedBy supervisor
        transport.Start(handler)
        
            
            