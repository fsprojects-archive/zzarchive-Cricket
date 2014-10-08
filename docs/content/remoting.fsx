(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor
open System.Net

(**
Ping - Pong with Remoting
=========================

**A full working example is available [here](https://github.com/fsprojects/FSharp.Actor/tree/master/src/Samples/Remoting/PingPong)**

This example consists of two actors Ping and Pong that exchange a set of messages. When the Ping actor is created
a counter is initialized in this case to 100000. Once this counter reaches zero the messages stop flowing and the 
actors shutdown. The message cascade is started by the Ping actor which sends a Ping message to the Pong actor, 
which then returns a Pong message back to the Ping actor, which then decreaments its count.
*)

(**
As with any actor system, we need to define the message types. With remoting these types need to be put in a seperate assembly
when using binary serialization, so they can be shared between the processes. If you are using JSON or XML this need not be 
the case. In the PingPong sample we create a seperate assembly called PingPongDomain and enter the following types definition.
*)

type PingPong =
    | Ping
    | Pong
    | Stop

(**
To configure the a process that contains actors for remoting, we need to configure the transports that are available 
to the host. In this case we are using the built in TCP transport. 

Once the actor host has started we need to create a system to hold the actor and enable remoting on that system, by
calling the `EnableRemoting` method. This method takes two parameters. The first parameter defines how actor systems 
will communicate with each other when looking up actors. The second defines how the actor systems discover each other.
*)
let system = ActorHost.Start()
                      .SubscribeEvents(fun (evnt:ActorEvent) -> printfn "%A" evnt)
                      .EnableRemoting(
                            [new TCPTransport(TcpConfig.Default(IPEndPoint.Create(12002)))],
                            new TcpActorRegistryTransport(TcpConfig.Default(IPEndPoint.Create(12003))),
                            new UdpActorRegistryDiscovery(UdpConfig.Default(), 1000)
                      )

(** 
Once the hosts and systems are setup with the transports configured. The implementation of the actors 
stays identical to the in-process implementation.
*)

let ping count =
    actor {
        name "ping"
        body (
                let pong = !~"pong"
                let rec loop count = 
                    messageHandler {
                        let! msg = Message.receive()
                        match msg with
                        | Pong when count > 0 ->
                              if count % 1000 = 0 then printfn "Ping: ping %d" count
                              do! Message.post pong.Value Ping
                              return! loop (count - 1)
                        | Ping -> failwithf "Ping: received a ping message, panic..."
                        | _ -> pong.Value <-- Stop
                    }
                
                loop count        
           ) 
    }

let pong = 
    actor {
        name "pong"
        body (
            let rec loop count = messageHandler {
                let! msg = Message.receive()
                match msg with
                | Ping -> 
                      if count % 1000 = 0 then printfn "Pong: ping %d" count
                      do! Message.reply Pong
                      return! loop (count + 1)
                | Pong _ -> failwithf "Pong: received a pong message, panic..."
                | _ -> ()
            }
            loop 0        
        ) 
    }

(**
One thing to consider when remoting is enabled on a actor system, is the latency that maybe introduced when actors
are resolved. Depending on how qualified the actor path is, the lookup may have to wait for a reply from all of the 
peers that the actor system has discovered. For more information about actor lookups see [here](actor_lookup.html)
*)
