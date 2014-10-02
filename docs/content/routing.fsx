(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

(**

Routing
=======

TODO...
*)

ActorHost.Start()

let actorDefinition id = 
    actor {
        body (
          let rec loop() =
              messageHandler {
                  let! msg = Message.receive None
                  do printfn "%s handled %s" id msg
                  return! loop()
              }
          loop()
        )
    }

let actors = 
    [1..5] 
    |> List.map (fun i -> actorDefinition (string i) |> Actor.start) 
    |> Routing.roundRobin

for i in 1..10 do
    actors <-- "Msg " + (string i)