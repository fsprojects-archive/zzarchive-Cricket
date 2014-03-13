// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

// Sample Surge Server

open System
open System.Collections.Concurrent
open FSharp.Actor
open FSharp.Actor.Surge
open Surge.Common.Messages
open Surge.Common.Agents

let surgeTransport = 
    new SurgeTransport(1338, ?log = Some Logging.Silent)

let quoteProducer(product: string, broadcaster: BroadcastAgent<string>) = 
    async {
        let rec produceQuote(count: int) =
            do Async.Sleep(250) |> Async.RunSynchronously
            broadcaster.Agent.Post(sprintf "%s|%u" product count, None)
            produceQuote(count + 1)
        produceQuote(0)
    }

let mattActor =
    Actor.spawn (Actor.Options.Create("matts-actor", ?logger = Some Logging.Silent))
        (fun (actor:IActor<int64>) -> 
            let rec loop() =
                async {
                    let! (msg, sender) = actor.Receive()
                    Console.ForegroundColor <- ConsoleColor.Green
                    Console.WriteLine(msg)
                    Console.ForegroundColor <- ConsoleColor.White
                    return! loop()
                }
            loop()
        )

let connectionManager =
    Actor.spawn (Actor.Options.Create("server/connect", ?logger = Some Logging.Silent))
        (fun (actor:IActor<RequestWithPath>) ->
            let rec loop() =
                async {
                    let! (msg, sender) = actor.Receive()
                    Console.ForegroundColor <- ConsoleColor.Green
                    Console.WriteLine("Accepted client connection")
                    Console.ForegroundColor <- ConsoleColor.White
                    msg.ResponsePath ?<-- "Accepted"
                    return! loop()
                }
            loop()
        )

let subscriptionManager =
    let quoteBroadcasters = new ConcurrentDictionary<string, BroadcastAgent<string>>()

    Actor.spawn (Actor.Options.Create("server/subscriptions", ?logger = Some Logging.Silent))
        (fun (actor:IActor<RequestWithPath>) ->
            let rec loop() =
                async {
                    let! (msg, sender) = actor.Receive()
                    match msg.Request with
                    | Subscribe(product) -> 
                        if quoteBroadcasters.ContainsKey(product) then
                            match quoteBroadcasters.TryGetValue(product) with
                            | (true, broadcaster) -> broadcaster.AddBroadcastee(msg.ResponsePath)
                            | _ -> Console.WriteLine("error")
                        else
                            let broadcaster = new BroadcastAgent<string>(product)
                            broadcaster.AddBroadcastee(msg.ResponsePath)
                            quoteBroadcasters.TryAdd(product, broadcaster) |> ignore
                            quoteProducer(product, broadcaster) |> Async.Start
                    
                    | _ -> msg.ResponsePath ?<-- "Other message type"
                    return! loop();
                }
            loop()
        )

[<EntryPoint>]
let main argv = 
    Registry.Transport.register surgeTransport

    Console.ReadLine() |> ignore

    0 // return an integer exit code
