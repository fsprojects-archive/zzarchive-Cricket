(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

(**
*)

ActorHost.Start()

let system = ActorHost.CreateSystem("pingpong")
                      .SubscribeEvents(fun (evnt:ActorEvent) -> printfn "%A" evnt) 

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
                      !~msg.Sender <-- Pong
                      return! loop (count + 1)
                | Pong -> failwithf "Pong: received a pong message, panic..."
                | _ -> return ()
            }
            loop 0        
        ) 
    }

let pingRef = system.SpawnActor(ping 10000)
let pongRef = system.SpawnActor(pong)

pingRef <-- Pong

pingRef <-- Restart
pongRef <-- Restart
