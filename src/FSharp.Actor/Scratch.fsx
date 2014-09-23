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

fsi.AddPrinter(fun (x:System.DateTime) -> x.ToString("dd/MM/yyyy HH:mm:ss.fffffff"))
fsi.AddPrintTransformer(fun (x:Diagnostics.TraceHeader) -> (x.Annotation, x.ParentId, x.SpanId, System.DateTime(x.Timestamp)) |> box)
fsi.AddPrintTransformer(fun (x:(string * seq<Diagnostics.TraceHeader>)) -> (x |> fst, x |> snd |> Seq.toList) |> box)

config.GetTraces()
|> Seq.groupBy (fun t -> t.ParentId)
|> Seq.map (fun (p, ts) -> p, ts |> Seq.sortBy(fun x -> x.Timestamp))
|> Seq.toList