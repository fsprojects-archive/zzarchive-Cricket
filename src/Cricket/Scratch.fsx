#I "../../bin"
#r "Cricket.dll"

open System
open Cricket
open Cricket.Diagnostics

ActorHost.Start()

let writer = 
    actor {
        body (
            let rec loop() = 
                messageHandler {
                  let! msg = Message.receive()
                  do printfn "%s" msg
                  return! loop()
                }
            loop()
        )
    } |> Actor.spawn

let actorDefinition id = 
    actor {
        body (
          let rec loop() =
              messageHandler {
                  let! msg = Message.receive()
                  do! Message.post writer (sprintf "%s handled %s" id msg)
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
