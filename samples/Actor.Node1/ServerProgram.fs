// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

//Actor.Node1

open System
open System.Net
open FSharp.Actor
open Messages

let fractureTransport = 
    new Fracture.FractureTransport(1337)

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




let server =
    Actor.spawn (Actor.Options.Create("TheFSharpDojo"))
        (fun (actor:IActor<ConnectionMessage>) ->
            let clients : Map<string, string> = Map.empty
            let log = (actor :?> Actor.T<ConnectionMessage>).Log
            let rec loop(clients : Map<string, string>) =
                async {
                    let! (msg, sender) = actor.Receive()
                    match msg with
                        | Connect(username, clientAddress) -> 
                            return! loop(clients.Add(username, clientAddress))
                        | Disconnect(username) -> return! loop(clients.Remove(username))
                        
                }
            loop(Map.empty)
        )

[<EntryPoint>]
let main argv = 
    Registry.Transport.register fractureTransport
    
//    logger <-- "Hello"
//    "node1/logger" ?<-- "Hello"

    while Console.ReadLine() <> "exit" do
        "actor.fracture://127.0.0.1:6666/node2/logger" ?<-- "Ping"

    "actor.fracture://127.0.0.1:6666/node2/logger" ?<!- Shutdown("Remote Shutdown")
    
    Console.ReadLine() |> ignore

    0