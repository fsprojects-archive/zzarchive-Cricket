(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

ActorHost.Start().SubscribeEvents(fun (e:ActorEvent) -> printfn "%A" e)

(**

Supervisors and Error Recovery
=============================

Code fails, occasionally :). In a conventional programming model you would typically trap this error by using a try/catch statement around the code that could fail and
then decided the appropriate action for each of the type of exceptions. The same is true of actors, within a message handling loop we can define try/catch blocks,
*)

let actorWithExceptionHandling = 
    actor {
        name "i_handle_exceptions"
        body (
            let rec loop() = messageHandler {
                let! msg = Message.receive None

                try
                    failwithf "Lets, go bang - %s" msg
                with e -> 
                    printf "Foo %A" e

                return! loop()
                
            }
            loop()
        )
    } |> Actor.spawn

actorWithExceptionHandling <-- "Errrorrr"

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

let supervisor = 
    supervisor {
        name "supervisor"
        link (!~"actor1")
        strategy Supervisor.oneForAll
    } |> Supervisor.spawn

actor1 <-- "Bang!"

let actor2 = failingActor "actor2" |> Actor.spawn

Supervisor.link actor2 supervisor

actor2 <-- "Bang!"