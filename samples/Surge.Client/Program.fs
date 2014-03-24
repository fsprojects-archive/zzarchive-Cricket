// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

// Sample Surge Client

open System
open System.Configuration
open System.Drawing
open System.Windows.Forms
open FSharp.Actor
open FSharp.Actor.Surge
open Surge.Common.Messages

let surgeTransport = 
    new SurgeTransport(1337)

let serverAddress =
    match ConfigurationManager.AppSettings.Item("ServerAddress") with
    | null -> "127.0.0.1:1338"
    | (value) -> value

let clientAddress =
    match ConfigurationManager.AppSettings.Item("ClientAddress") with
    | null -> "127.0.0.1:1337"
    | (value) -> value

let subscribe(product) =
    let subscriptionPath = sprintf "subscriptions/%s/%s" product (Guid.NewGuid().ToString())
        
    Async.Start(
        async {
            let form = new Form(Width = 225, Height = 100, Text = product)
            let context = System.Threading.SynchronizationContext.Current
            let label = new Label()
            form.Controls.Add(label)

            let updateLabel(text: string) =
                async {
                    do! Async.SwitchToContext(context)
                    label.Text <- text
                }

            let receiver = 
                Actor.spawn (Actor.Options.Create(subscriptionPath, ?logger = Some Logging.Silent))
                    (fun (actor:IActor<string>) ->
                        let rec loop() =
                            async {
                                let! (msg, sender) = actor.Receive()
                                do! updateLabel(msg)
                                return! loop()
                            }
                        loop()
                    )

            receiver.OnStopped.Add(fun e ->
                Console.WriteLine(sprintf "Disposing client subscription to %s" product)
            )

            form.Closed.Add(fun e ->
                do sprintf "actor.surge://%s/server/subscriptions" serverAddress ?<-- { Request = Unsubscribe(product); ResponsePath = sprintf "actor.surge://%s/%s" clientAddress subscriptionPath }
                receiver.PostSystemMessage(Shutdown("Disposing subscription"), None)
            )

            Application.Run(form)
        }
    )

    do sprintf "actor.surge://%s/server/subscriptions" serverAddress ?<-- { Request = Subscribe(product); ResponsePath = sprintf "actor.surge://%s/%s" clientAddress subscriptionPath }



[<EntryPoint>]
let main argv = 
    Registry.Transport.register surgeTransport

    while true do
        Console.WriteLine("\nEnter the name of a stock symbol to subscribe to quote updates: ")
        let symbol = Console.ReadLine()
        subscribe(symbol) |> ignore
    
    0 // return an integer exit code
