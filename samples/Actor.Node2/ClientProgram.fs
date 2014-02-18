// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

//Actor.Node2

open System
open System.Net
open FSharp.Actor
open Messages

//let fractureTransport = 
//    new Fracture.FractureTransport(31337)

let logger = 
    Actor.spawn (Actor.Options.Create("node2/logger")) 
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

let client =
    Actor.spawn (Actor.Options.Create("CodeRonin")) 
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

[<EntryPoint>]
let main argv = 
    Console.WriteLine "Enter listening port number for client: "
    let clientPortNumber =
        let rec getClientPort(input) =
            match Int32.TryParse(input) with
            | (true, port) -> port
            | _ -> 
                Console.WriteLine ("\tInvalid")
                getClientPort(Console.ReadLine())
        getClientPort (Console.ReadLine())

        
    Registry.Transport.register (new Fracture.FractureTransport(clientPortNumber))
    do "actor.fracture://127.0.0.1:1337/TheFSharpDojo" ?<-- Connect("CodeRonin", (String.Format("actor.fracture://127.0.0.1:{0}/{1}", 31337, "CodeRonin")))

    while Console.ReadLine () <> "exit" do
        Async.Sleep(100) |> Async.RunSynchronously

    Console.ReadLine() |> ignore

    0
