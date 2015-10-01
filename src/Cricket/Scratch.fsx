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

#load "Framing.fs"
open System
open Cricket

let data = Array.init 4096 byte

let frame (bufferSize:int) (payload:byte[]) =
    let bufferSize = bufferSize - 8 // since any data prefixed with 2 ints
    let noOfFrames = (payload.Length / bufferSize) + 1
    let rec frame' count state (payload:byte[]) =
        if count = noOfFrames
        then state |> List.rev 
        else
            let nextCount = count + 1
            let newState = 
                [|
                    yield! BitConverter.GetBytes(count)
                    yield! BitConverter.GetBytes(noOfFrames)
                    yield! payload.[count * bufferSize .. (min (nextCount * bufferSize) (payload.Length - 1))]
                |] :: state
            frame' nextCount newState payload

    frame' 0 [] payload

let framedData = frame 4096 data

let unframe = ()



