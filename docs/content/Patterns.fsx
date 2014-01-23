(*** hide ***)
#load "Dependencies.fsx"
open FSharp.Actor

(**
#Patterns

Create actors that represent a common coding pattern.
*)

(**
##Dispatch

###Round robin
Round robin dispatch, distributes messages in a round robin fashion to its workers. 
*)

let createWorker i =
    Actor.spawn (Actor.Options.Create(sprintf "workers/worker_%d" i)) (fun (actor:IActor<int>) ->
        let log = (actor :?> Actor.T<int>).Log
        let rec loop() = 
            async {
                let! (msg,_) = actor.Receive()
                do log.Debug(sprintf "Actor %A recieved work %d" actor msg, None)
                do! Async.Sleep(5000)
                do log.Debug(sprintf "Actor %A finshed work %d" actor msg, None)
                return! loop()
            }
        loop()
    )

let workers = [|1..10|] |> Array.map createWorker
let rrrouter = Patterns.Dispatch.roundRobin<int> "workers/routers/roundRobin" workers

[1..10] |> List.iter ((<--) rrrouter)

(**
###Shortest Queue
Shortest queue, attempts to find the worker with the shortest queue and distributes work to them. For constant time
work packs this will approximate to round robin routing.

Using the workers defined above we can define another dispatcher but this time using the shortest queue
dispatch strategy
*)

let sqrouter = Patterns.Dispatch.shortestQueue "workers/routers/shortestQ" workers

[1..100] |> List.iter ((<--) sqrouter)

