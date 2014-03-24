// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

// Sample Surge Server

open System
open System.Collections.Generic
open FSharp.Actor
open FSharp.Actor.Surge
open Surge.Common.Messages

let surgeTransport = 
    new SurgeTransport(1338)

let createQuoteProducer(product: string, broadcaster: IActor) = 
    async {
        let rec produceQuote(count: int) =
            do Async.Sleep(250) |> Async.RunSynchronously
            broadcaster.Post(sprintf "%s Quote %u" product count, None)
            produceQuote(count + 1)
        produceQuote(0)
    }

let createQuoteBroadcaster(product) =
    Actor.spawn (Actor.Options.Create(sprintf "server/subscriptions/broadcasters/%s" product, ?logger = Some Logging.Silent))
        (fun (actor:IActor<string>) ->
            let rec loop() =
                async {
                    let! (msg, sender) = actor.Receive()
                    actor.Children <-* msg
                    return! loop()
                }
            loop()
        )

let createQuoteSubscription(product, responsePath) =
    Actor.spawn (Actor.Options.Create(sprintf "server/subscriptions/%s/%s" product responsePath, ?logger = Some Logging.Silent))
        (fun (actor:IActor<string>) ->
            let rec loop() =
                async {
                    let! (msg, sender) = actor.Receive()
                    responsePath ?<-- msg
                    return! loop()
                }
            loop()
        )

let subscriptionManager =
    let quoteBroadcasters = new Dictionary<string, IActor>()

    Actor.spawn (Actor.Options.Create("server/subscriptions", ?logger = Some Logging.Silent))
        (fun (actor:IActor<RequestWithPath>) ->
            let rec loop() =
                async {
                    let! (msg, sender) = actor.Receive()
                    match msg.Request with
                    
                    | Subscribe(product) -> 
                        Console.WriteLine(sprintf "\nAdding subscriber %s for %s quote stream" msg.ResponsePath product) 
                        if quoteBroadcasters.ContainsKey(product) then
                            match quoteBroadcasters.TryGetValue(product) with
                            | (true, broadcaster) -> 
                                broadcaster.Link (createQuoteSubscription(product, msg.ResponsePath))
                            | _ -> Console.WriteLine("error")
                        else
                            let broadcaster = createQuoteBroadcaster(product)
                            broadcaster.Link(createQuoteSubscription(product, msg.ResponsePath))
                            quoteBroadcasters.Add(product, broadcaster) 
                            createQuoteProducer(product, broadcaster) |> Async.Start

                    | Unsubscribe(product) ->
                         if quoteBroadcasters.ContainsKey(product) then
                            match quoteBroadcasters.TryGetValue(product) with
                            | (true, broadcaster) ->
                                Console.WriteLine(sprintf "\nRemoving subscriber %s from %s quote stream" msg.ResponsePath product) 
                                broadcaster.UnLink !! (sprintf "server/subscriptions/%s/%s" product msg.ResponsePath) 
                            | _ -> Console.WriteLine("error")
                    
                    | _ -> msg.ResponsePath ?<-- "Other message type"
                    
                    return! loop();
                }
            loop()
        )

[<EntryPoint>]
let main argv = 
    Registry.Transport.register surgeTransport
    Console.ReadLine() |> ignore
    0
