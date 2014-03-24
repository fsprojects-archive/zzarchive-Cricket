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

type SurgeTransport(listenPort: int, ?asyncMode:bool, ?logger:ILogger) =
    let scheme = "actor.surge"
    let logger = defaultArg logger Logging.Console
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

        let tryGetOrConnectClient() = async {
            match client.Connected with
            | true -> return Choice1Of2 client
            | false ->
                try
                    client.Connect(endpoint)
                    return Choice1Of2 client
                with e ->
                    return Choice2Of2 e
        }
        
        let proxy = 
            Actor.spawn(Actor.Options.Create(?logger = Some Logging.Silent))
                (fun (actor:IActor<_>) -> 
                    let rec loop() =
                        async {
                            let! (msg, sender) = actor.Receive()
                            let! clientChoice = tryGetOrConnectClient()
                            match clientChoice with
                            | Choice1Of2(client) ->
                                let rm = { Target = address; Sender = sender |> Option.map (fun s -> s.Path); Body = msg }
                                let memoryStream = new MemoryStream()
                                fsp.Serialize(memoryStream, rm)
                                let bytes = memoryStream.ToArray()
                                let header = BitConverter.GetBytes(bytes.Length)
                                let message = 
                                    [ header; bytes ]
                                    |> Array.concat
                     
                                client.GetStream().Write(message, 0, message.Length)
                                return! loop()

                            | Choice2Of2(e) ->
                                logger.Warning(sprintf "Could not establish connection for message sending to %s. Disposing proxy actor for this destination" (endpoint.ToString()), Some e)
                                actor.PostSystemMessage(Shutdown(sprintf "Could not establish connection for message sending to %s." (endpoint.ToString())), None)
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
                | None -> logger.Warning(sprintf "Could not resolve actor %s" (msg.Target.ToString()), None)
    }

    let unpackSurgeMessage(binaryMessage: byte[]) = async {
        try
            let msg = fsp.Deserialize<SurgeMessage>(new MemoryStream(binaryMessage))
            match asyncMode with
            | true -> processSurgeMessage(msg) |> Async.Start
            | false -> processSurgeMessage(msg) |> Async.RunSynchronously
        with e ->
            logger.Error("Error deserializing SurgeMessage from Transport", Some(e))
    }

    let readFromClient(client: TcpClient) =
        async {
            try
                let headerBuffer = Array.create 4 0uy
                let messageBuffer = Array.create 1024 0uy
                let clientStream = client.GetStream()

                while true do
                    let! headerBytesRead = clientStream.AsyncRead(headerBuffer, 0, 4)
                    let messageSize = BitConverter.ToInt32(headerBuffer, 0)

                    let rec tryReadMessage(bytesRead: int, message: byte[]) = 
                        async {
                            try
                                if bytesRead < messageSize then
                                    let! msgBytesRead = clientStream.AsyncRead(messageBuffer, 0, Math.Min(messageBuffer.Length, messageSize - bytesRead)) 
                                    let msg =
                                        messageBuffer
                                        |> Seq.take(msgBytesRead)
                                        |> Seq.toArray
                                    let combined = 
                                        [message; msg]
                                        |> Array.concat
                                    return! tryReadMessage(bytesRead + msgBytesRead, combined)
                                else
                                    return Choice1Of2 message
                            with e ->
                                return Choice2Of2 e
                        }

                    let! message = tryReadMessage(0, Array.empty)
                
                    match message with
                    | Choice1Of2(bytes) -> 
                        match asyncMode with
                        | true -> unpackSurgeMessage(bytes) |> Async.Start
                        | false -> unpackSurgeMessage(bytes) |> Async.RunSynchronously

                    | Choice2Of2(ex) ->
                            raise ex
            with e ->
                logger.Error(sprintf "TcpClient listener for %s encountered an unexpected error" ((client.Client.RemoteEndPoint :?> IPEndPoint).Address.ToString()), Some e)
        }

    do Async.Start(
        async {
            let clientListener = new TcpListener(IPAddress.Any, listenPort)
            clientListener.Start()

            while true do
                let! client = clientListener.AsyncAcceptTcpClient()
                readFromClient(client) |> Async.Start
        }
    )

    interface ITransport with
        member val Scheme = scheme with get

        member x.CreateRemoteActor(remoteAddress) =
            RemoteActor.spawn remoteAddress x (Actor.Options.Create(?logger = Some Logging.Console))

        member x.Send(remoteAddress, msg, sender) =
            match proxyTransportActors.TryGetValue(remoteAddress) with
            | true, actor -> actor.Post(msg, sender)
            | _ ->
                let proxyActor = createRemoteProxyActor(remoteAddress)
                proxyTransportActors.TryAdd(remoteAddress, proxyActor) |> ignore

                proxyActor.OnStopped.Add(fun e ->
                    proxyTransportActors.TryRemove(remoteAddress) |> ignore
                )
                proxyActor.Post(msg, sender)
                
        member x.SendSystemMessage(remoteAddress, msg, sender) = 
            match proxyTransportActors.TryGetValue(remoteAddress) with
            | true, actor -> actor.Post(msg, sender)
            | _ ->
                let proxyActor = createRemoteProxyActor(remoteAddress)
                proxyActor.Post(msg, sender)
                proxyTransportActors.TryAdd(remoteAddress, proxyActor) |> ignore
            