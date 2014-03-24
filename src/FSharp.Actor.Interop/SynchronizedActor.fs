namespace FSharp.Actor.Interop

open System
open System.Threading
open FSharp.Actor

type SynchronizedActor<'a> (name: string, context: SynchronizationContext, computation: Action<'a>) =
    let executeOnContext(message: 'a) = 
        async {
            do! Async.SwitchToContext(context)
            computation.Invoke(message)
        }
    
    let actor =
        Actor.spawn(Actor.Options.Create(name))
            (fun (actor: IActor<'a>) ->
                let rec loop() =
                    async {
                        let! (msg, sender) = actor.Receive()
                        do! executeOnContext(msg)
                        return! loop()
                    }
                loop()
            )

    member x.Actor =
        actor

    interface IDisposable with
        member x.Dispose() =
            actor.PostSystemMessage (Shutdown(sprintf "%s has been disposed" name), None)


