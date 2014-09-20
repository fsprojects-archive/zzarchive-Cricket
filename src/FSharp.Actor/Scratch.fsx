#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor
open System.IO
open System.Threading

(**

Metrics
=======

*)

ActorHost.Start()

type Say =
    | Hello
    | HelloWorld
    | Name of string

type PingPong =
    | Ping
    | Pong
    | Stop

let ping count =
    actor {
        name "ping"
        body (
                let pong = !~"pong"

                let rec loop count = 
                    messageHandler {
                        let! msg = Actor.receive None
                        match msg with
                        | Pong when count > 0 ->
                              if count % 1000 = 0 then printfn "Ping: ping %d" count
                              do! Async.Sleep(1000)
                              do! Actor.replyTo pong.Value Ping
                              return! loop (count - 1)
                        | Ping -> failwithf "Ping: received a ping message, panic..."
                        | _ -> 
                            do! Actor.replyTo pong.Value Stop
                            ()
                    }
                
                loop count        
           ) 
    }

let pong = 
    actor {
        name "pong"
        body (
            let rec loop count = messageHandler {
                let! msg = Actor.receive None
                match msg with
                | Ping -> 
                      if count % 1000 = 0 then printfn "Pong: ping %d" count
                      do! Async.Sleep(1000)
                      do! Actor.reply Pong
                      return! loop (count + 1)
                | Pong _ -> failwithf "Pong: received a pong message, panic..."
                | _ -> ()
            }
            loop 0        
        ) 
    }

let pingRef = Actor.spawn (ping 1)
let pongRef = Actor.spawn pong

pingRef <-- Pong


