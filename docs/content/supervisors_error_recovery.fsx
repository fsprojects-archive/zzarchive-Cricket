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

let failingActor id = 
    actor { 
        name id
        body (
            let rec loop() = messageHandler {
                let! msg = Message.receive None
                failwithf "Lets, go bang - %s" msg
                return! loop()
            }
            loop()
        )
    }
 
let actor1 = failingActor "actor1" |> Actor.spawn  
let actor2 = failingActor "actor2" |> Actor.spawn

let supervisor = 
    supervisor {
        name "supervisor"
        link (!~"actor1")
        strategy Supervisor.oneForAll
    } |> Supervisor.spawn

actor1 <-- "Bang!"

Supervisor.link actor2 supervisor

actor2 <-- "Bang!"