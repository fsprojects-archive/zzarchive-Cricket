// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open System
open FSharp.Actor
open FSharp.Actor.Surge


let fractureTransport = 
    new Fracture.FractureTransport(6667)

let surgeTransport = 
    new SurgeTransport(1337)

let logger = 
    Actor.spawn (Actor.Options.Create("node1/logger")) 
       (fun (actor:IActor<string>) ->
            let log = (actor :?> Actor.T<string>).Log
            let rec loop() = 
                async {
                    let! (msg, sender) = actor.Receive()
                    log.Debug(sprintf "%A sent %A" sender msg, None)
                    match sender with
                    | Some(s) -> 
                        s <-- "pong"
                    | None -> ()
                    return! loop()
                }
            loop()
        )

let remoteSurgeActor =
    Actor.spawn(Actor.Options.Create("surge/server"))
       (fun (actor:IActor<string>) ->
            let rec loop() =
                async {
                    let! (msg, sender) = actor.Receive()
                }    
       ) 

[<EntryPoint>]
let main argv = 
    Registry.Transport.register fractureTransport
    Registry.Transport.register surgeTransport
    
    logger <-- "Hello"
    "node1/logger" ?<-- "Hello"

    while Console.ReadLine() <> "exit" do
        "actor.fracture://127.0.0.1:6666/node2/logger" ?<-- "Ping"

    "actor.fracture://127.0.0.1:6666/node2/logger" ?<!- Shutdown("Remote Shutdown")
    
    Console.ReadLine() |> ignore

    0