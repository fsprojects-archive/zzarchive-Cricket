(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

(**
Ping - Pong
===========

This example consists of two actors Ping and Pong that exchange a set of messages. When the Ping actor is created
a counter is initialized in this case to 100000. Once this counter reaches zero the messages stop flowing and the 
actors shutdown. The message cascade is started by the Ping actor which sends a Ping message to the Pong actor, 
which then returns a Pong message back to the Ping actor, which then decrements its count.
*)

ActorHost.Start()
         .SubscribeEvents(fun (evnt:ActorEvent) -> printfn "%A" evnt) |> ignore

type PingPong =
    | Ping
    | Pong
    | Stop

let ping count =
    actor {
        name "ping"
        messageHandler (fun cell ->
            let pong = !!"pong"
            printfn "Resolved pong: %A" pong
            let rec loop count = async {
                let! msg = cell.Receive()
                match msg.Message with
                | Pong when count > 0 -> 
                      if count % 1000 = 0 then cell.Logger.Info("Ping: pong")
                      pong <-- Ping
                      return! loop (count - 1)
                | Ping -> failwithf "Ping: received a ping message, panic..."
                | _ -> 
                    pong <-- Stop
                    return ()
            }
            
            loop count        
        ) 
    }

let pong = 
    actor {
        name "pong"
        messageHandler (fun cell ->
            let rec loop count = async {
                let! msg = cell.Receive()
                match msg.Message with
                | Ping -> 
                      if count % 1000 = 0 then cell.Logger.Info("Pong: ping " + (count.ToString()))
                      msg.Sender <-- Pong
                      return! loop (count + 1)
                | Pong -> failwithf "Pong: received a pong message, panic..."
                | _ -> return ()
            }
            loop 0        
        ) 
    }

let pingRef = Actor.spawn (ping 10000)
let pongRef = Actor.spawn (pong)

pingRef <-- Pong