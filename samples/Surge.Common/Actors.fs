namespace Surge.Common.Actors

open FSharp.Actor

type BroadcastActor<'a>(actorName: string) =
    let broadcastees = ref Set.empty<string>  

    let broadcaster = 
        Actor.spawn(Actor.Options.Create(actorName, ?logger = Some Logging.Silent))
            (fun (actor: IActor<'a>) ->
                let rec loop() =
                    async {
                        let! (msg, sender) = actor.Receive()
                        !broadcastees
                        |> Seq.iter(fun i -> i ?<-- msg)
                        return! loop()
                    }
                loop()
            )

    member x.AddSubscriber(remotePath) =
        broadcastees := (!broadcastees).Add(remotePath)

    member x.RemoveSubscriber(remotePath) =
        broadcastees := (!broadcastees).Remove(remotePath)

    member x.Agent =
        broadcaster