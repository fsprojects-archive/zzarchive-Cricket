namespace FSharp.Actor

module Patterns =
    
    open FSharp.Actor
    open FSharp.Actor.Types

    module Dispatch = 

        let shortestQueue name (refs : seq<IActor>) =
            Actor.spawn (Actor.Options.Create(name)) (fun (actor:IActor<_>) -> 
                let rec loop() =
                    async {
                        let! (msg,sender) = actor.Receive()
                        (actor.Children |> Seq.minBy (fun x -> x.QueueLength)).Post(msg, sender)
                        return! loop()
                    }
                loop()
            ) |> Actor.link refs


        let roundRobin<'a> name (refs : IActor[]) =
            Actor.spawn (Actor.Options.Create(name)) (fun (actor:IActor<'a>) ->
                let rec loop indx = 
                    async {
                        let! (msg,sender) = actor.Receive()
                        refs.[indx].Post(msg,sender)
                        return! loop ((indx + 1) % refs.Length)
                    }
                loop 0
            ) |> Actor.link refs

    module Routing =
       
        let route name (router : 'msg -> seq<IActor<_>> option) =
            Actor.spawn name (fun (actor:IActor<'msg>) ->
                async {
                    let! (msg,sender) = actor.Receive()
                    match router msg with
                    | Some(targets) -> targets |> Seq.iter (fun (t:IActor<'a>) -> t.Post(msg, sender))
                    | None -> ()
                }
            )

        let broadcast name (targets:seq<IActor>) =
            Actor.spawn name (fun (actor:IActor<_>) ->
                async {
                    let! msg = actor.Receive()
                    do targets |> Seq.iter (fun (t:IActor) -> t.Post(msg))
                }
            )

    
    let map name (f : 'a -> 'b) (target:IActor) = 
        Actor.spawn name (fun (actor:IActor<'a>) ->
            async {
                let! (msg,sender) = actor.Receive()
                target.Post(f msg, sender)
            }
        ) 

    let filter name (f : 'a -> bool) (target:IActor) = 
        Actor.spawn name (fun (actor:IActor<'a>) ->
            async {
                let! (msg,sender) = actor.Receive()
                if (f msg) then target.Post(msg,sender)
            }
        )
