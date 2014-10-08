namespace FSharp.Actor 

open System
open System.Threading
open System.Collections.Generic
open FSharp.Actor

#if INTERACTIVE
open FSharp.Actor
open FSharp.Actor.Diagnostics
#endif

module Routing = 
    
    let roundRobin (refs : 'a) =
        let selection = ActorSelection.op_Implicit refs 
        actor {
            name "system/router/roundrobin"
            body (
                let rec loop (q:Queue<_>) = messageHandler {
                    let! msg = Message.receive()
                    let target = q.Dequeue()
                    do! Message.post target msg
                    do q.Enqueue(target)
                    return! loop q
                }
                loop (new Queue<_>(selection.Refs))
            )
        } |> Actor.start |> ActorSelection.op_Implicit

