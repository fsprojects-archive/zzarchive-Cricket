namespace FSharp.Actor

module Patterns =
    
    open FSharp.Actor
    open FSharp.Actor.DSL

    module Dispatch = 

        let shortestQueue name (refs : seq<ActorRef>) =
            Actor.spawn name (fun (actor:Actor<_>) -> 
                async {
                    let! msg = actor.Receive()
                    (actor.Children |> Seq.minBy (fun x -> x.QueueLength)) <-- msg
                }
            ) |> Actor.link refs


        let roundRobin<'a> name (refs : ActorRef[]) =
            Actor.spawn name (fun (actor:Actor<'a>) ->
                async {
                    let! msg = actor.Receive()
                    let indx = actor?ChildIndex
                    refs.[indx] <-- msg
                    actor?ChildIndex <- (indx + 1)
                }
            ) |> Actor.link refs

    module Routing =

        let route name (router : 'msg -> seq<ActorRef> option) =
            Actor.spawn name (fun (actor:Actor<'msg>) ->
                async {
                    let! msg = actor.Receive()
                    match router msg with
                    | Some(targets) -> targets <-* msg
                    | None -> ()
                }
            )

        let broadcast name (targets:seq<ActorRef>) =
            Actor.spawn name (fun (actor:Actor<_>) ->
                async {
                    let! msg = actor.Receive()
                    do targets <-* msg
                }
            )

    
    let map name (f : 'a -> 'b) (target:ActorRef) = 
        Actor.spawn name (fun (actor:Actor<'a>) ->
            async {
                let! msg = actor.Receive()
                target <-- (f msg)
            }
        ) 

    let filter name (f : 'a -> bool) (target:ActorRef) = 
        Actor.spawn name (fun (actor:Actor<'a>) ->
            async {
                let! msg = actor.Receive()
                if (f msg) then target <-- msg
            }
        )
