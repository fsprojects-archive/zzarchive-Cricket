// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

// Sample Surge Server

open System
open FSharp.Actor
open FSharp.Actor.Surge
open Surge.Common

let surgeTransport = 
    new SurgeTransport(1338)

let connectionManager =
    Actor.spawn (Actor.Options.Create("server/connect"))
        (fun (actor:IActor<ResponsePathMessage>) ->
            let rec loop() =
                async {
                    let! (msg, sender) = actor.Receive()
                    Console.WriteLine("Accepted client connection")
                    msg.ResponsePath ?<-- "Accepted"
                    return! loop()
                }
            loop()
        )

[<EntryPoint>]
let main argv = 
    Registry.Transport.register surgeTransport

    Console.ReadLine() |> ignore

    0 // return an integer exit code
