#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

ActorHost.Start().SubscribeEvents(fun (evnt:ActorEvent) -> printfn "%A" evnt) |> ignore

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
                        let! msg = Message.receive None
                        match msg with
                        | Pong when count > 0 ->
                              if count % 1000 = 0 then printfn "Ping: ping %d" count
                              do! Message.replyTo pong.Value Ping
                              return! loop (count - 1)
                        | Ping -> failwithf "Ping: received a ping message, panic..."
                        | _ -> 
                              do! Message.replyTo pong.Value Stop
                              return ()
                    }
                
                loop count        
           ) 
    }

let pong = 
    actor {
        name "pong"
        body (
            let rec loop count = messageHandler {
                let! msg = Message.receive None
                match msg with
                | Ping -> 
                      if count % 1000 = 0 then printfn "Pong: ping %d" count
                      do! Message.reply Pong
                      return! loop (count + 1)
                | Pong _ -> failwithf "Pong: received a pong message, panic..."
                | _ -> return ()
            }
            loop 0        
        ) 
    }

let pingRef = Actor.spawn (ping 2)
let pongRef = Actor.spawn pong

pingRef <-- Pong

let config = Diagnostics.Trace.getConfig().Tracer :?> (Diagnostics.InMemoryTraceSink)

config.GetTraces()
|> Seq.sortBy (fun x -> x.Timestamp)
//|> Seq.groupBy (fun t -> t.ParentId)
|> Seq.toList