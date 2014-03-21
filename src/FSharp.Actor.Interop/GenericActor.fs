namespace FSharp.Actor.Interop

open System
open FSharp.Actor

type GenericActor<'a> (name: string, computation: Action<'a>) =
    let actor = 
        Actor.spawn(Actor.Options.Create(name))
            (fun (actor: IActor<'a>) ->
                let rec loop() =
                    async {
                        let! (msg, sender) = actor.Receive()
                        do computation.Invoke(msg)
                        return! loop()
                    }
                loop()
            )

    member x.Actor =
        actor

    interface IDisposable with
        member x.Dispose() =
            actor.PostSystemMessage (Shutdown(sprintf "%s has been disposed" name), None)
