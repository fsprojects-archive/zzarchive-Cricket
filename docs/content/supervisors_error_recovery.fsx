(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "Cricket.dll"
open System
open Cricket

(**

Supervisors and Error Recovery
------------------------------

Code fails. In a conventional programming model you would typically trap this error by using a try/catch statement around the code that could fail and
then decided the appropriate action for each of the type of exceptions. The same is true of actors, within a message handling loop we can define try/catch blocks,
*)

ActorHost.Start().SubscribeEvents(fun (e:ActorEvent) -> printfn "%A" e)

(**
__Note:__ here we are subscribing to actor events, so we get a log of when an actor changes status. 
*)

let actorWithExceptionHandling = 
    actor {
        name "i_handle_exceptions"
        body (
            let rec loop() = messageHandler {
                let! msg = Message.receive()

                try
                    invalidOp (sprintf "Lets, go bang - %s" msg)
                with 
                   | :? InvalidOperationException as e -> printf "Safetly caught %A" e

                return! loop()
            }
            loop()
        )
    } |> Actor.spawn

actorWithExceptionHandling <-- "Errrorrr"

(**
This is all very well and good, but what if an unexpected error occurs, another type of exception. In this case our actor is going to simply shutdown and stop responding to any messages
which is far from ideal. So what are the options in this case; we could continue adding matches until all possible cases are covered or take the pokemon (gotta catch them all) exception handling approach 
and just catch everything. These would have the effect of making our actor loop robust but at the cost of determinisism. Additionally what if other actors need to respond to this actor erroring, this
actor would have to now know about all other actors that __could__ be effected by this, leading to a very tightly coupled design. 

Enter Supervisors
-----------------

Supervisors are a concept well known in the [erlang community](http://www.erlang.org/doc/man/supervisor.html) although similar there are a few minor differences in the Cricket implementation see [here](supervisors_error_recovery.html#Differences-From-Erlang). 
Essentially a supervisor does exactly what it says on the tin. It supervisors other actors. If an actor errors and is `linked` to a supervisor then the supervisor will tell that actor to either 
shutdown or restart depending on the strategy it has been set. Of course these aren't the only options but that will be [covered later](supervisors_error_recovery.html#supervisor-strategies).

To give an example of supervisors in action, lets take the `actorWithExceptionHandling` from before and remove the exception handling.   
*)

let failingActor1 = 
    actor { 
        name "failing_actor_1"
        body (
            let rec loop() = messageHandler {
                let! msg = Message.receive()
                failwithf "Lets, go bang - %s" msg
                return! loop()
            }
            loop()
        )
    } |> Actor.spawn  

failingActor1 <-- "Bang!"

(**
this actor is now started and if I send a message to it it will simply throw and exception and shutdown. Running this in interactive, gives the following output,

    val it : unit = ()
    > ActorErrored
      (ActorRef actor://HP20024950_Fsi_3856@*/failing_actor_1,
       System.Exception: Lets, go bang - Bang!
       at Microsoft.FSharp.Core.Operators.FailWith[T](String message)
       at FSI_0003.loop@66-2.Invoke(String _arg1) in D:\Appdev\Cricket\docs\content\supervisors_error_recovery.fsx:line 66
       at Cricket.MessageModule.Bind@109-2.Invoke(a2 _arg5) in D:\Appdev\Cricket\src\Cricket\Message.fs:line 109
       at Microsoft.FSharp.Control.AsyncBuilderImpl.args@797-1.Invoke(a a))
    ActorShutdown (ActorRef actor://HP20024950_Fsi_3856@*/failing_actor_1)

from this point onwards it is not possible to send another message to this actor as it has been shutdown. To prevent this we can link this actor to a supervisor.
*)


let supervisor = 
    supervisor {
        name "supervisor"
        link failingActor1
        strategy Supervisor.oneForOne
    } |> Supervisor.spawn

failingActor1 <-- "Bang!"

(**
Now when we send a message to out `failingActor1` reference we get the following

    ActorLinked
      (ActorRef actor://HP20024950_Fsi_7664@*/supervisor,
       ActorRef actor://HP20024950_Fsi_7664@*/failing_actor_1)

this is the supervisor creating a link to the actor reference, telling it to redirect any errors or shutdown traps to this supervisor. And also,
    
    val it : unit = ()
    > ActorErrored
      (ActorRef actor://HP20024950_Fsi_7664@*/failing_actor_1,
       System.Exception: Lets, go bang - Bang!
       at Microsoft.FSharp.Core.Operators.FailWith[T](String message)
       at FSI_0003.loop@66-2.Invoke(String _arg1) in D:\Appdev\Cricket\docs\content\supervisors_error_recovery.fsx:line 66
       at Cricket.MessageModule.Bind@109-2.Invoke(a2 _arg5) in D:\Appdev\Cricket\src\Cricket\Message.fs:line 109
       at Microsoft.FSharp.Control.AsyncBuilderImpl.args@797-1.Invoke(a a))
    ActorRestart (ActorRef actor://HP20024950_Fsi_7664@*/failing_actor_1)
    ActorStarted (ActorRef actor://HP20024950_Fsi_7664@*/failing_actor_1)

which is nearly the same as before but notice the two additional events at the end of the trace. This is the supervisor restarting the actor so it is 
able to consume more messages.

An important thing to notice here is that when we defined the actor we statically referenced the actor we wanted to link to, aditionally that actor 
must also be started. This is because the actor needs to send a `SystemMessage` to the actor telling it to `Link` to the supervisor. This however is
not desirable in all systems. Some systems can be far more dynamic in nature. To overcome this static linking restriction we can dynamically link actors. 
*)

let dynamicsupervisor = 
    supervisor {
        name "dynamic_supervisor"
        strategy Supervisor.oneForOne
    } |> Supervisor.spawn

Supervisor.link failingActor1 dynamicsupervisor

failingActor1 <-- "Bang!"

(**
Which has now restored the same behaviour as before. In addition to creating links between actors we can also unlink them. 
*)

Supervisor.unlink failingActor1 dynamicsupervisor

failingActor1 <-- "Bang!"

(**
Once an actor is unlinked it is on its own. Sending a message to this actor that causes an error would once again cause the actor to shutdown. 

There is a couple of interesting things to note about linking and not linking actors. 

* Actors can only be linked to a single supervisor - If I called link on the same actor ref to two different supervisors `A` then `B` then only supervisor `B`
will recieve messages from the actor. However the actor will still be known to `A` thus it is possible for `A` to shutdown the actor if another actor under supervisor `A`
supervision fails.

* Multiple calls to unlink an actor from a supervisor will have no effect, and if the actor is not there in the first place no error will be thrown.

* If the actor strategy is such that the supervisor tells the actor to shutdown the supervisor will still hold a reference to that actor.  

Supervisor Strategies
---------------------

Supervisor Stratgies define the way in which the supervisor handles errors from its children. By default Cricket defines the following.

###Fail

This strategy simply just shutsdown the actor that errored. Not really any different to the actor failing on its own.

###Fail All

When any actor in the supervisors tree fails, the supervisor will shutdown all other actors in the tree.

###OneForOne

This is the default. When an actor in the supervisor tree errors that actor is simply restarted. No messages are sent to any other actors in the tree.

###OneForAll

When any actor in the tree encounters an error the supervisor will restart all of the other actors. 
*)

(**
Differences from Erlang
-----------------------

In erlang actors control the startup, shutdown and restarting of actors. This is not the case in Cricket. In Cricket supervisors do indeed have the ability
to shutdown and restart the actors, and actors will notify supervisors on errors, shutdown and restart events but they do not control the lifecycle of the actors. 

*)