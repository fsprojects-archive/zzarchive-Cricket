(*** hide ***)
#load "Dependencies.fsx"
open FSharp.Actor
open FSharp.Actor.DSL

(**
#Patterns

Create actors that represent a common coding pattern.

*)


(**
##Dispatch

##Round robin
Round robin dispatch, distributes messages in a round robin fashion to its workers. 
*)

let createWorker i =
    Actor.spawn (ActorPath.create (sprintf "workers/worker_%d" i)) (fun (actor:Actor<int>) ->
        async {
            let! msg = actor.Receive()
            do actor.Log.Debug(sprintf "Actor %A recieved work %d" actor msg)
            do! Async.Sleep(5000)
            do actor.Log.Debug(sprintf "Actor %A finshed work %d" actor msg)
        }
    )

let workers = [|1..10|] |> Array.map createWorker
let rrrouter = Patterns.Dispatch.roundRobin<int> (ActorPath.create "workers/routers/roundRobin") workers

[1..10] |> List.iter ((<--) rrrouter)

(**
##Shortest Queue
Shortest queue, attempts to find the worker with the shortest queue and distributes work to them. For constant time
work packs this will approximate to round robin routing.

Using the workers defined above we can define another dispatcher but this time using the shortest queue
dispatch strategy
*)

let sqrouter = Patterns.Dispatch.shortestQueue (ActorPath.create "workers/routers/shortestQ") workers

[1..100] |> List.iter ((<--) sqrouter)

