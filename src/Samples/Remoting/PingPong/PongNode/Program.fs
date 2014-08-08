// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open System
open System.Net
open FSharp.Actor
open PingPong

ActorHost.Start([new TCPTransport(TcpConfig.Default(IPEndPoint.Create(12000)))])

let system = ActorHost.CreateSystem("pingpong")
                      .SubscribeEvents(fun (evnt:ActorEvent) -> printfn "%A" evnt)
                      .EnableRemoting(
                            new TcpActorRegistryTransport(TcpConfig.Default(IPEndPoint.Create(12001))),
                            new UdpActorRegistryDiscovery(UdpConfig.Default(), 1000)
                      )

let pong = 
    actor {
        name "pong"
        messageHandler (fun cell ->
            let rec loop count = async {
                let! msg = cell.Receive()
                match msg.Message with
                | Ping -> 
                      if count % 1000 = 0 then cell.Logger.Info("Pong: ping " + (count.ToString()))
                      msg.Sender <-- Pong
                      return! loop (count + 1)
                | Pong _ -> failwithf "Pong: received a pong message, panic..."
                | _ -> return ()
            }
            loop 0        
        ) 
    }

[<EntryPoint>]
let main argv = 

    system.SpawnActor(pong) |> ignore

    Console.WriteLine("Press enter to exit")
    Console.ReadLine() |> ignore
    0 // return an integer exit code