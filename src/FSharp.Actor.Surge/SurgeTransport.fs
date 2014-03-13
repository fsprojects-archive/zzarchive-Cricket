namespace FSharp.Actor.Surge

open System
open System.IO
open System.Net
open System.Net.Sockets
open FSharp.Actor
open System.Threading
open System.Collections.Concurrent
open FSharp.Actor
open FSharp.Actor.Types
open FSharp.Actor.Remoting
open FsPickler

[<AutoOpen>]
module TcpExtensions =
    type System.Net.Sockets.TcpListener with
        member x.AsyncAcceptTcpClient() =
            Async.FromBeginEnd(x.BeginAcceptTcpClient, x.EndAcceptTcpClient)

type SurgeMessage = {
    Sender : ActorPath option
    Target : ActorPath
    Body : obj
}


type SurgeTransport(listenPort: int, ?asyncMode:bool, ?log:ILogger) =
    let scheme = "actor.surge"
    let log = defaultArg log Logging.Console
    let asyncMode = defaultArg asyncMode false
    let proxyTransportActors = new ConcurrentDictionary<Uri, IActor>()
    let fsp = new FsPickler()

    let parseAddress(address: Uri) =
        match Net.IPAddress.TryParse(address.Host) with
        | true, ip -> ip, address.Port
        | _ -> failwithf "Invalid Address %A expected address of form {IPAddress}:{Port} eg 127.0.0.1:8080" address

    let createRemoteProxyActor(address: Uri) =
        let ip, port = parseAddress address
        let endpoint = new IPEndPoint(ip, port)
        let client = new TcpClient(new IPEndPoint(IPAddress.Any, 0))

        let rec connectClient() = async {
            match client.Connected with
            | true -> return client
            | false ->
                try
                    client.Connect(endpoint)
                    return client
                with e ->
                    log.Error(sprintf "Error establishing remote connection in proxy actor: %s" e.Message, Some(e))
                    return! connectClient()
        }
        
        let proxy = 
            Actor.spawn(Actor.Options.Create())
                (fun (actor:IActor<_>) -> 
                    let rec loop() =
                        async {
                            let! (msg, sender) = actor.Receive()
                            let! connectedClient = connectClient()
                            let rm = { Target = address; Sender = sender |> Option.map (fun s -> s.Path); Body = msg }
                            let memoryStream = new MemoryStream()
                            fsp.Serialize(memoryStream, rm)
                            let bytes = memoryStream.ToArray()
                            let header = BitConverter.GetBytes(bytes.Length)
                            let message = 
                                [ header; bytes ]
                                |> Array.concat
                     
                            connectedClient.GetStream().Write(message, 0, message.Length)
                            return! loop()
                        }
                    loop()
                )

        proxy

    let processSurgeMessage(msg: SurgeMessage) = async {
        match Registry.Actor.tryFind (Path.toLocal msg.Target)  with
                | Some(a) -> 
                    match msg.Body with
                    | :? SystemMessage as msg -> 
                        a.PostSystemMessage(msg, Some a)
                    | msg -> a.Post(msg, Some a)
                | None -> log.Warning("Could not resolve actor", None)
    }

    let unpackSurgeMessage(binaryMessage: byte[]) = async {
        try
            let msg = fsp.Deserialize<SurgeMessage>(new MemoryStream(binaryMessage))
            match asyncMode with
            | true -> processSurgeMessage(msg) |> Async.Start
            | false -> processSurgeMessage(msg) |> Async.RunSynchronously
        with e ->
            log.Error("Error deserializing SurgeMessage from Transport", Some(e))
    }

    // probably want to take a look at this and wrap it up in a type or something
    do Async.Start(async { 
            let clientListener = new TcpListener(IPAddress.Loopback, listenPort)
            clientListener.Start()
            while true do
                let! client = clientListener.AsyncAcceptTcpClient()
                let clientStream = client.GetStream()
               
                do Async.Start(async {
                let headerBuffer = Array.create 4 0uy
                let messageBuffer = Array.create 1024 0uy
                
                while true do
                    let! headerBytesRead = clientStream.AsyncRead(headerBuffer, 0, 4)
                    let messageSize = BitConverter.ToInt32(headerBuffer, 0)

                    let rec readMessage(bytesRead: int, message: byte[]) = 
                        async {
                            if bytesRead < messageSize then
                                let! msgBytesRead = clientStream.AsyncRead(messageBuffer, 0, Math.Min(messageBuffer.Length, messageSize - bytesRead)) 
                                let msg =
                                    messageBuffer
                                    |> Seq.take(msgBytesRead)
                                    |> Seq.toArray
                                let combined = 
                                    [message; msg]
                                    |> Array.concat
                                return! readMessage(bytesRead + msgBytesRead, combined)
                            else
                                return message
                        }

                    let! message = readMessage(0, Array.empty)
                    match asyncMode with
                    | true -> unpackSurgeMessage(message) |> Async.Start
                    | false -> unpackSurgeMessage(message) |> Async.RunSynchronously
                })
        })

    interface ITransport with
        member val Scheme = scheme with get

        member x.CreateRemoteActor(remoteAddress) =
            RemoteActor.spawn remoteAddress x (Actor.Options.Create(?logger = Some Logging.Silent))

        member x.Send(remoteAddress, msg, sender) =
            match proxyTransportActors.TryGetValue(remoteAddress) with
            | true, actor -> actor.Post(msg, sender)
            | _ ->
                let proxyActor = createRemoteProxyActor(remoteAddress)
                proxyActor.Post(msg, sender)
                proxyTransportActors.TryAdd(remoteAddress, proxyActor) |> ignore

        member x.SendSystemMessage(remoteAddress, msg, sender) = 
            match proxyTransportActors.TryGetValue(remoteAddress) with
            | true, actor -> actor.Post(msg, sender)
            | _ ->
                let proxyActor = createRemoteProxyActor(remoteAddress)
                proxyActor.Post(msg, sender)
                proxyTransportActors.TryAdd(remoteAddress, proxyActor) |> ignore
            