(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

(**

Supervisors and Error Recovery
=============================

TODO...
*)

ActorHost.Start().SubscribeEvents(fun (e:ActorEvent) -> printfn "%A" e)

let naughtyActor = 
    actor { 
        name "naughtyActor"
        body (
            let rec loop() = messageHandler {
                let! msg = Message.receive None
                failwithf "Lets, go bang again %s" msg
                return! loop()
            }
            loop()
        )
    } |> Actor.spawn

let supervisor = 
    supervisor {
        name "supervisor"
        link naughtyActor
        strategy Supervisor.oneForOne
    } |> Supervisor.spawn

naughtyActor <-- "Bang!"