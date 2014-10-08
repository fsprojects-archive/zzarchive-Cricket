(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"

#I @"D:\Appdev\Foogle.Charts\packages\FSharp.Data.2.0.9\lib\net40"
#r "FSharp.Data.dll"

#I @"D:\Appdev\Foogle.Charts\bin\"
#r @"Foogle.Charts.dll"
#load @"Foogle.Charts.fsx"

open System
open FSharp.Actor
open FSharp.Actor.Diagnostics
open Foogle

(**
Tracing
-------
*)

let traceWriter = new InMemoryTraceWriter()
let rnd = new Random()

ActorHost.Start(tracing = TracingConfiguration.Create(enabled = true, writer = traceWriter))


type Message =
    | Request of Guid option * string
    | Response of Guid option * string 

let middleTier responseString = actor {
    body (
        let rec loop() = messageHandler {
            let! msg = Message.receive()
            match msg with
            | Request(Some(id),payload) ->
                 do! Async.Sleep(rnd.Next(500, 3000))
                 do! Message.reply (Response(Some id, responseString + payload))
                 return! loop()
            | _ -> return! loop()
        }
        loop()
    )
}

let bRef = 
    actor { 
        inherits (middleTier "response_B_")
        name "B"
    } |> Actor.spawn

let cRef = 
    actor { 
        inherits (middleTier "response_C_")
        name "C"
    } |> Actor.spawn

let frontEnd = 
    actor {
        name "A"
        body (
            let middleTier = !~["B"; "C"]
            let rec loop state = messageHandler {
                let! msg = Message.receive()
                let! sender = Message.sender()
                match msg with
                | Request(Some(id),payload) ->
                     do! Async.Sleep(rnd.Next(500, 3000))
                     do! Message.post middleTier (Request(Some id, payload))
                     return! loop (Map.add id (sender,[]) state)
                | Response(Some id, payload) ->
                    match state.TryFind(id) with
                    | Some(s,[]) -> return! loop (Map.add id (s,[payload]) state)
                    | Some(s,[h]) -> 
                        do! Message.post s (Response(Some(id), h + "_" + payload))
                        return! loop (Map.remove id state)
                    | _ -> return! loop state
                | _ -> return! loop state
                return! loop state     
            }
           loop Map.empty
        )
    } |> Actor.spawn

let client = 
    actor {
        name "client"
        body (
            let frontend = !~"A"
            let rec loop() = messageHandler {
                let! msg = Message.receive()
                match msg with
                | Request(_,msg) ->
                    do! Async.Sleep(rnd.Next(500, 3000)) 
                    do! Message.post frontend (Request(Some (Guid.NewGuid()), msg))
                    return! loop()
                | Response(_,payload) -> 
                    do! Async.Sleep(rnd.Next(500, 900))
                    printfn "Received response %s" payload
                    return! loop()
            }
            loop()
        )
    } |> Actor.spawn


client <-- Request(None, "Hello")

let rawTraces = traceWriter.GetTraces()

let timeLine() = 
    rawTraces
    |> Seq.groupBy (fun t -> t.SpanId)
    |> Seq.collect (fun (_, ts) -> 
         ts 
         |> Seq.groupBy (fun x -> x.SpanId) 
         |> Seq.choose (fun (_,t) -> 
             let t = t |> Seq.sortBy (fun x -> x.Timestamp) |> Seq.toList
             match t with
             | [h;t] -> Some(t.Actor.Path, t.Sender.Path, (DateTime h.Timestamp), (DateTime t.Timestamp))
             | _ -> failwithf "Unexpected paring %A" t
         )
         |> Seq.toArray
        )
    |> Seq.toList

let data = timeLine()


Chart.Timeline(data, rowLabels = true, barLabels = false)
|> Chart.WithTitle("Ping - Pong messages")