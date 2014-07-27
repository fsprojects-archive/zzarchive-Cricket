#load "FSharp.Actor.fsx"

open System.Net
open FSharp.Actor
open FSharp.Actor.Remoting

Remoting.enable (TcpConfig.Default(IPEndPoint.Create(6667)), 
                 UdpConfig.Default(), 
                 [new TCPTransport(TcpConfig.Default(IPEndPoint.Create(7000)))],
                  Async.DefaultCancellationToken)

ActorHost.reportEvents(fun (evnt:ActorEvents) -> printfn "Event: %A" evnt)
ActorHost.start()

let node2Actor =
    actor {
        path "dispatcher"
        messageHandler (fun ctx -> 
            let log = ctx.Logger
           
            let rec loop () = async {
                let! msg = ctx.Receive()
                printfn "Message: %A" msg
                match msg.Message with
                | SendToPing msg ->
                    let remoteActor = !!"ping" 
                    printfn "Sending to %A %s" remoteActor msg
                    remoteActor <-- msg
                | KeepLocal msg -> printfn "Recieved %s" msg 
                return! loop()
            }
            loop())
    } |> Actor.spawn

let dispatcher = !!"dispatcher"

!!"dispatcher" <-- SendToPing "Hello, from node 2"

let p = !!"ping"
