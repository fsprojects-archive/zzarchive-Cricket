// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open System
open System.Net
open FSharp.Actor
open PingPong

ActorHost.Start([new TCPTransport(TcpConfig.Default(IPEndPoint.Create(12002)))])

let system = ActorHost.CreateSystem("pingpong")
                      .SubscribeEvents(fun (evnt:ActorEvent) -> printfn "%A" evnt)
                      .EnableRemoting(
                            new TcpActorRegistryTransport(TcpConfig.Default(IPEndPoint.Create(12003))),
                            new UdpActorRegistryDiscovery(UdpConfig.Default(), 1000)
                      )
let ping count =
    actor {
        name "ping"
        messageHandler (fun cell ->
            let pong = !!"pong"
            printfn "Resolved pong: %A" pong
            let rec loop count = async {
                let! msg = cell.Receive()
                match msg.Message with
                | Pong when count > 0 -> 
                      if count % 100 = 0 then cell.Logger.Info("Ping: pong")
                      pong <-- Ping
                      return! loop (count - 1)
                | Ping -> failwithf "Ping: received a ping message, panic..."
                | _ -> 
                    pong <-- Stop
                    return ()
            }
            
            loop count        
        ) 
    }

[<EntryPoint>]
let main argv = 
    Console.WriteLine("Press enter to start")
    Console.ReadLine() |> ignore

    let pingRef = system.SpawnActor(ping 1000)
    pingRef <-- Pong

    Console.WriteLine("Press enter to exit")
    Console.ReadLine() |> ignore
    0 // return an integer exit code
