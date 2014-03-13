// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

// Sample Surge Client

open System
open System.Drawing
open System.Windows.Forms
open FSharp.Actor
open FSharp.Actor.Surge
open Surge.Common.Messages

let surgeTransport = 
    new SurgeTransport(1337, ?log = Some Logging.Silent)

let confirmConnect =
    Actor.spawn (Actor.Options.Create("confirm"))
        (fun (actor:IActor<string>) -> 
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

let subscribe(product) =
    let subscriptionPath = sprintf "subscriptions/%s" product
        
    Async.Start(async {
        let form = new Form(Width = 150, Height = 100, Text = product)
        let context = System.Threading.SynchronizationContext.Current
        let label = new Label()
        form.Controls.Add(label)

        let updateLabel(text: string) =
            async {
                do! Async.SwitchToContext(context)
                label.Text <- text
            }

        let receiver = 
            Actor.spawn (Actor.Options.Create(subscriptionPath))
                (fun (actor:IActor<string>) ->
                    let rec loop() =
                        async {
                            let! (msg, sender) = actor.Receive()
                            do! updateLabel(msg)
                            return! loop()
                        }
                    loop()
                )
        Application.Run(form)
    })

    do "actor.surge://127.0.0.1:1338/server/subscriptions" ?<-- { Request = Subscribe(product); ResponsePath = sprintf "actor.surge://127.0.0.1:1337/%s" subscriptionPath }



[<EntryPoint>]
let main argv = 
    Registry.Transport.register surgeTransport

    while true do
        Console.WriteLine("\nEnter the name of a stock symbol to subscribe to quote updates: ")
        let symbol = Console.ReadLine()
        subscribe(symbol) |> ignore
    
//    let rec subscribeProduct() =
//        Console.WriteLine("Enter the name of a stock symbol to subscribe to quote updates: ")
//        let symbol = Console.ReadLine()
//        subscribe(symbol) |> ignore
//        subscribeProduct()
//    subscribeProduct()
//
//    Console.ReadLine() |> ignore

    0 // return an integer exit code
