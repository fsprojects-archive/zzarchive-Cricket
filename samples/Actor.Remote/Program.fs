// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open System
open System.IO
open System.Text
open System.Security.Principal
open System.Net
open System.Net.Sockets
open FSharp.Actor
open FSharp.Actor.DSL

[<EntryPoint>]
let main argv = 
    
    let mutable exit = false
    let listenPort = ref 0
    let cts = new Threading.CancellationTokenSource()

    let actor = 
        Actor.spawn (ActorPath.create "remoteactor") (fun (actor:Actor<string>) ->
            async {
                let! msg = actor.Receive()
                printfn "Actor Recieved message %A (port: %d)" msg !listenPort
            }
        )

    Console.Write("Set Listener Port: ")
    listenPort := int( Console.ReadLine())

    Remoting.registerTransport Serialisers.Binary (new FractureTransport({ Port = (Some !listenPort) }))
   
    while not <| exit do
         Console.Write("Enter Message (format = url;message, enter Exit to exit): ")
         let input = Console.ReadLine()
         if input.ToLower() = "exit" 
         then exit <- true
         else 
         match input.ToLower().Split([|','|], StringSplitOptions.RemoveEmptyEntries) with
         | [|address;msg|] ->
            try
                !!address <-- msg
            with
                | :? ActorNotFound as a -> printfn "%s" a.Message 
         | _ -> printfn "Invalid input format"
          
    0 // return an integer exit code

