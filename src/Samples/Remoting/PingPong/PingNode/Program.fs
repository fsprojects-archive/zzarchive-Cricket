// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open FSharp.Actor

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

[<EntryPoint>]
let main argv = 
    printfn "%A" argv
    0 // return an integer exit code
