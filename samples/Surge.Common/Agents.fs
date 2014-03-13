namespace Surge.Common.Agents

open FSharp.Actor

type BroadcastAgent<'a>(agentName: string) =
    let broadcastees = ref Set.empty<string>  

    let broadcaster = 
        Actor.spawn(Actor.Options.Create(agentName))
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

    member x.AddBroadcastee(remotePath) =
        broadcastees := (!broadcastees).Add(remotePath)

    member x.RemoveBroadcastee(remotePath) =
        broadcastees := (!broadcastees).Remove(remotePath)

    member x.Agent =
        broadcaster