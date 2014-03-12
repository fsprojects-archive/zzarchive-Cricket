// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

// Sample Surge Client

open System
open FSharp.Actor
open FSharp.Actor.Surge
open Surge.Common

let surgeTransport = 
    new SurgeTransport(1337)

let confirmConnect =
    Actor.spawn (Actor.Options.Create("confirm"))
        (fun (actor:IActor<string>) -> 
            let rec loop() =
                async {
                    let! (msg, sender) = actor.Receive()
                    Console.WriteLine(msg)
                    return! loop()
                }
            loop()
        )

[<EntryPoint>]
let main argv = 
    Registry.Transport.register surgeTransport

    "actor.surge://127.0.0.1:1338/server/connect" ?<-- { Message = "Hi"; ResponsePath = "actor.surge://127.0.0.1:1337/confirm" }

    Console.ReadLine() |> ignore

    0 // return an integer exit code
