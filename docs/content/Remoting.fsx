(*** hide ***)
#load "Dependencies.fsx"

open FSharp.Actor
open FSharp.Actor.Fracture

(**
#Remoting

Remoting can be used to send messages to connected actor systems outside of the current process boundary.
To use remoting we must first register a transport and a serialiser to use with that transport. Transports are responsible
for packaging, sending and recieving messages to/from other remote systems. Each transport should have a scheme thast uniquely 
identifies it. To register a transport, do something like the following 
*)

let transport = new FractureTransport(8080) :> ITransport
Registry.Transport.register transport

(**
The above call registers a transport that uses [Fracture](https://github.com/fractureio/fracture). When a transport is created it
is wrapped in a actor of `Actor<RemoteMessage>` and path `transports/{scheme}` in the case of 
the fracture transport this would be `transports/actor.fracture`. This actor is then supervised by the `system/remotingsupervisor` actor, which
is initialised the `OneForOne` strategy so will restart the transport if it errors at any point.

Sending a message to a remote actor is identical to sending messages to local actors apart from the actor path has to be fully qualified.
*)

"actor.fracture://127.0.0.1:8081/RemoteActor" ?<-- "Some remote actor message"

(**
In addition to sending normal messages to a remote actor, we can also send system messages. For example if we want to restart a remote actor
We could send the following message
*)

"actor.fracture://127.0.0.1:8081/RemoteActor" ?<-- Restart

(**
//TODO: More to come, as implemented remote supervision, deployment 
*)