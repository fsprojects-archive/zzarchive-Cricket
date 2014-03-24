namespace FSharp.Actor.Fracture

open System
open FSharp.Actor
open System.Threading
open System.Collections.Concurrent
open FSharp.Actor
open FSharp.Actor.Types
open FSharp.Actor.Remoting

open Fracture
open Fracture.Common

type FractureMessage = {
    Sender : ActorPath option
    Target : ActorPath
    Body : obj
}

type FractureTransport(listenPort:int, ?serialiser:ISerialiser,?log:ILogger) = 
    let serialiser = defaultArg serialiser (Serialisers.Pickler)
    let log = defaultArg log Logging.Console
    let scheme = "actor.fracture"

    let tryUnpackFractureMessage (msg) =
        try
            Choice1Of2 (serialiser.Deserialise<FractureMessage> msg)
        with e ->
            Choice2Of2 e
        
    let onReceived(msg:byte[], server:TcpServer, socketDescriptor:SocketDescriptor) =
        if msg <> null then
            
            match tryUnpackFractureMessage msg with
            | Choice1Of2(message) ->
                match Registry.Actor.tryFind (Path.toLocal message.Target)  with
                | Some(a) -> 
                    match message.Body with
                    | :? SystemMessage as msg -> 
                        a.PostSystemMessage(msg, Some a)
                    | msg -> a.Post(msg, Some a)
                | None -> log.Warning(sprintf "%A recieved message for %A from %A but could not resolve actor" scheme message.Target (socketDescriptor.RemoteEndPoint.Address.ToString()), None)
            | Choice2Of2(e) ->
                log.Warning(sprintf "Error deserializing fracture message: %s" (msg.ToString()), Some e)

//            let messageChoice = serialiser.Deserialise<FractureMessage> msg 
    //        if msg <> null
    //        then 
    //            let msg = msg |> unbox<FractureMessage>
//            match Registry.Actor.tryFind (Path.toLocal message.Target)  with
//            | Some(a) -> 
//                match message.Body with
//                | :? SystemMessage as msg -> 
//                    a.PostSystemMessage(msg, Some a)
//                | msg -> a.Post(msg, Some a)
//            | None -> log.Warning(sprintf "%A recieved message for %A from %A but could not resolve actor" scheme message.Target (socketDescriptor.RemoteEndPoint.Address.ToString()), None)


    do
        try
            let l = Fracture.TcpServer.Create(onReceived)
            l.Listen(Net.IPAddress.Any, listenPort)
            log.Debug(sprintf "%A transport listening on %d" scheme listenPort, None)
        with e -> 
            log.Error(sprintf "%A failed to create listener on port %d" scheme listenPort, Some e)
            reraise()

    let clients = new ConcurrentDictionary<Uri, Fracture.TcpClient>()

    let tryResolve (address:Uri) (dict:ConcurrentDictionary<_,_>) = 
        match dict.TryGetValue(address) with
        | true, v -> Some v
        | _ -> None

    let getOrAdd (ctor:Uri -> Async<_>) (address:Uri) (dict:ConcurrentDictionary<Uri,_>) =
        async {
            match dict.TryGetValue(address) with
            | true, v -> return Choice1Of2 v
            | _ -> 
                let! instance = ctor address
                match instance with
                | Choice1Of2 v -> return Choice1Of2 (dict.AddOrUpdate(address, v, (fun address _ -> v)))
                | Choice2Of2 e -> return Choice2Of2 e 
        }

    let remove (address:Uri) (dict:ConcurrentDictionary<Uri,'a>) = 
        match dict.TryRemove(address) with
        | _ -> ()

    let parseAddress (address:Uri) =
        match Net.IPAddress.TryParse(address.Host) with
        | true, ip -> ip, address.Port
        | _ -> failwithf "Invalid Address %A expected address of form {IPAddress}:{Port} eg 127.0.0.1:8080" address

    let tryCreateClient (address:Uri) =
        async {
            try
                let ip,port = parseAddress address
                let endpoint = new Net.IPEndPoint(ip,port)
                let connWaitHandle = new AutoResetEvent(false)
                let client = new Fracture.TcpClient()
                client.Connected |> Observable.add(fun x -> log.Debug(sprintf "%A client connected on %A" scheme x, None); connWaitHandle.Set() |> ignore) 
                client.Disconnected |> Observable.add (fun x -> log.Debug(sprintf "%A client disconnected on %A" scheme x, None); remove address clients)
                client.Start(endpoint)
                let! connected = Async.AwaitWaitHandle(connWaitHandle, 10000)
                if not <| connected 
                then return Choice2Of2(TimeoutException() :> exn)
                else return Choice1Of2 client
            with e ->
                return Choice2Of2 e
        }

    interface ITransport with
        member val Scheme = scheme with get

        member x.CreateRemoteActor(remoteAddress) = 
            RemoteActor.spawn remoteAddress x Actor.Options.Default

        member x.Send(remoteAddress, msg, sender) =
             async {
                let! client = getOrAdd tryCreateClient remoteAddress clients
                match client with
                | Choice1Of2(client) ->
                    let rm = { Target = remoteAddress; Sender = sender |> Option.map (fun s -> s.Path); Body = msg } 
                    let bytes = serialiser.Serialise rm
                    client.Send(bytes, true)
                | Choice2Of2 e -> log.Error(sprintf "%A transport failed to create client for send %A" scheme remoteAddress, Some e)
             } |> Async.Start

        member x.SendSystemMessage(remoteAddress, msg, sender) =
             async {
                let! client = getOrAdd tryCreateClient remoteAddress clients
                match client with
                | Choice1Of2(client) ->
                    let rm = { Target = remoteAddress; Sender = sender |> Option.map (fun s -> s.Path); Body = msg } 
                    client.Send(serialiser.Serialise rm, true)
                | Choice2Of2 e -> log.Error(sprintf "%A transport failed to create client for send system message %A" scheme remoteAddress, Some e)
             } |> Async.Start