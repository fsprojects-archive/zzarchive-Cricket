// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open System
open System.Net
open FSharp.Actor
open PingPong

let pong = 
    actor {
        name "pong"
        body (
            let rec loop count = messageHandler {
                let! msg = Actor.receive None
                match msg with
                | Ping -> 
                      if count % 1000 = 0 then printfn "Pong: ping %d" count
                      do! Actor.reply Pong
                      return! loop (count + 1)
                | Pong _ -> failwithf "Pong: received a pong message, panic..."
                | _ -> ()
            }
            loop 0        
        ) 
    }

[<EntryPoint>]
let main argv = 
    
    let transportPort = Int32.Parse(argv.[0])
    let registryTransportPort = Int32.Parse(argv.[1])
    let nodeName = argv.[2]
    
    ActorHost.Start(fun x -> x.AddTransports [new TCPTransport(TcpConfig.Default(IPEndPoint.Create(transportPort)))])
             .SubscribeEvents(fun (evnt:ActorEvent) -> printfn "%A" evnt)
             .EnableRemoting(
                   new TcpActorRegistryTransport(TcpConfig.Default(IPEndPoint.Create(registryTransportPort))),
                   new UdpActorRegistryDiscovery(UdpConfig.Default(), 1000)
             ) |> ignore

    Actor.spawn(pong) |> ignore

    Console.WriteLine("Press enter to exit")

    Console.ReadLine() |> ignore

    ActorHost.Dispose()

    Console.ReadLine() |> ignore

    0 // return an integer exit code