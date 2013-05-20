#load "Dependencies.fsx"
open FSharp.Actor
open FSharp.Actor.DSL

(**
#Basic Actors
*)

let multiplication = 
    (fun (actor:Actor<_>)  ->
        async {
            let! (a,b) = actor.Receive()
            let result = a * b
            do printfn "%A: %d * %d = %d" actor.Path a b result
        }
    )

let addition = 
    (fun (actor:Actor<_>) ->
        async {
            let! (a,b) = actor.Receive()
            let result = a + b
            do printfn "%A: %d + %d = %d" actor.Path a b result
        }
    )

let calculator = 
    [
       Actor.spawn (ActorPath.create "calculator/addition") addition
       Actor.spawn (ActorPath.create "calculator/multiplication") multiplication
    ]

(**
The above code creates two actors `calcualtor/addition` and `calculator/multiplication`

    actor://main-pc/calculator/addition pre-start Status: Shutdown
    actor://main-pc/calculator/addition started Status: OK
    actor://main-pc/calculator/multiplication pre-start Status: Shutdown
    actor://main-pc/calculator/multiplication started Status: OK
    
    val multiplication : actor:FSharp.Actor.Actor<int * int> -> Async<unit>
    val addition : actor:FSharp.Actor.Actor<int * int> -> Async<unit>
    val calculator : FSharp.Actor.ActorRef list =
      [actor://main-pc/calculator/addition;
       actor://main-pc/calculator/multiplication]

We can see that the actors state transitions are logged. For more information about Actor Events and Actor lifecycles see [here](ActorLifecycles.html)

Once we have created our actors we can be looked up by their path
*)
"calculator/addition" ?<-- (5,2)
"calculator/multiplication" ?<-- (5,2)

(**
Sending both of these messages yields

    actor://main-pc/calculator/addition: 5 + 2 = 7
    actor://main-pc/calculator/multiplication: 5 * 2 = 10

We can also send messages directly to actors if we have their `ActorRef`
*)

calculator.[0] <-- (5,2)

(**
This also yields 

    actor://main-pc/calculator/addition: 5 + 2 = 7

Or we could have broadcast to all of the actors in that collection
*)

calculator <-* (5,2)

(**
This also yields 

    actor://main-pc/calculator/addition: 5 + 2 = 7
    actor://main-pc/calculator/multiplication: 5 * 2 = 10

We can also resolve _systems_ of actors.
*)
"calculator/" ?<-- (5,2)

(**
This also yields 

    actor://main-pc/calculator/addition: 5 + 2 = 7
    actor://main-pc/calculator/multiplication: 5 * 2 = 10

However this actor wont be found because it does not exist
*)

"calculator/addition/foo" ?<-- (5,2)

(**
resulting in a `KeyNotFoundException`

    System.Collections.Generic.KeyNotFoundException: Could not find actor calculator/addition/foo  

We can also kill actors 
*)

"calculator/addition" ?<-- Die

(**
Sending now sending any message to the actor will result in an exception 

    System.InvalidOperationException: Actor (actor://main-pc/calculator/addition) could not handle message, State: Shutdown
*)


(**
#Changing the behaviour of actors

You can change the behaviour of actors at runtime.
*)

let rec schizoPing = 
    (fun (actor:Actor<_>) ->
        async {
            let! msg = actor.Receive()
            actor.Log.Info(sprintf "(%A): %A ping" actor msg)
            actor.Behave(schizoPong)
        }
    )
        
and schizoPong = 
    (fun (actor:Actor<_>) ->
        async {
            let! msg = actor.Receive()
            actor.Log.Info(sprintf "(%A): %A pong" actor msg)
            actor.UnBehave()
        }
    )

let schizo = Actor.spawn (ActorPath.create "schizo") schizoPing 

!!"schizo" <-- "Hello"

(**

Sending two messages to the 'schizo' actor results in

    (actor://main-pc/schizo): "Hello" ping

followed by

    (actor://main-pc/schizo): "Hello" pong


#Supervising Actors

Actors can supervise other actors, if we define an actor loop that fails on a given message
*)

let err = 
    (fun (actor:Actor<string>) ->
        async {
            let! msg = actor.Receive()
            if msg <> "fail"
            then printfn "%s" msg
            else failwithf "ERRRROROROR"
        }
    )

(**
then a supervisor will allow the actor to restart or terminate depending on the particular strategy that is in place

##Strategies

A supervisor strategy allows you to define the restart semantics for the actors it is watching

###OneForOne
    
A supervisor will only restart the actor that has errored
*)

let oneforone = 
    Actor.supervisor (ActorPath.create "oneforone") Supervisor.OneForOne [Actor.spawn (ActorPath.create "err_0") err]

!!"err_0" <-- "fail"

(**
This yields

    Restarting (OneForOne: actor://main-pc/err_0) due to error System.Exception: ERRRROROROR
       at FSI_0012.err@134-2.Invoke(String message) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 134
       at Microsoft.FSharp.Core.PrintfImpl.go@523-3[b,c,d](String fmt, Int32 len, FSharpFunc`2 outputChar, FSharpFunc`2 outa, b os, FSharpFunc`2 finalize, FSharpList`1 args, Int32 i)
       at Microsoft.FSharp.Core.PrintfImpl.run@521[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args)
       at Microsoft.FSharp.Core.PrintfImpl.capture@540[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args, Type ty, Int32 i)
       at Microsoft.FSharp.Core.PrintfImpl.gprintf[b,c,d,a](FSharpFunc`2 initialize, PrintfFormat`4 fmt)
       at FSI_0012.err@132-1.Invoke(String _arg1) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 134
       at Microsoft.FSharp.Control.AsyncBuilderImpl.args@753.Invoke(a a)
    actor://main-pc/err_0 pre-stop Status: Errored
    actor://main-pc/err_0 stopped Status: Shutdown
    actor://main-pc/err_0 pre-restart Status: Restarting
    actor://main-pc/err_0 re-started Status: OK

we can see in the last 4 lines that the supervisor restarted this actor.


###OneForAll

If any watched actor errors all children of this supervisor will be told to restart.
*)

let oneforall = 
    Actor.supervisor (ActorPath.create "oneforall") Supervisor.OneForAll 
        [
            Actor.spawn (ActorPath.create "err_1") err;
            Actor.spawn (ActorPath.create "err_2") err
        ]

!!"err_1" <-- "fail"

(**
This yields

    Restarting (OneForAll actor://main-pc/err_1) due to error System.Exception: ERRRROROROR
       at FSI_0004.err@134-2.Invoke(String message) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 134
       at Microsoft.FSharp.Core.PrintfImpl.go@523-3[b,c,d](String fmt, Int32 len, FSharpFunc`2 outputChar, FSharpFunc`2 outa, b os, FSharpFunc`2 finalize, FSharpList`1 args, Int32 i)
       at Microsoft.FSharp.Core.PrintfImpl.run@521[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args)
       at Microsoft.FSharp.Core.PrintfImpl.capture@540[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args, Type ty, Int32 i)
       at Microsoft.FSharp.Core.PrintfImpl.gprintf[b,c,d,a](FSharpFunc`2 initialize, PrintfFormat`4 fmt)
       at FSI_0004.err@132-1.Invoke(String _arg1) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 134
       at Microsoft.FSharp.Control.AsyncBuilderImpl.args@753.Invoke(a a)
    actor://main-pc/err_2 pre-stop Status: OK
    actor://main-pc/err_2 stopped Status: Shutdown
    actor://main-pc/err_2 pre-restart Status: Restarting
    actor://main-pc/err_2 re-started Status: OK
    actor://main-pc/err_1 pre-stop Status: Errored
    actor://main-pc/err_1 stopped Status: Shutdown
    actor://main-pc/err_1 pre-restart Status: Restarting
    actor://main-pc/err_1 re-started Status: OK

we can see here that all of the actors supervised by this actor has been restarted.

###Fail

A supervisor will terminate the actor that has errored
*)

let fail = 
    Actor.supervisor (ActorPath.create "fail") Supervisor.Fail 
        [
            Actor.spawn (ActorPath.create "err_1") err;
            Actor.spawn (ActorPath.create "err_2") err
        ]

!!"err_1" <-- "fail"

(**
This yields

    Terminating (AlwaysTerminate: actor://main-pc/err_1) due to error System.Exception: ERRRROROROR
       at FSI_0005.err@138-2.Invoke(String message) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 138
       at Microsoft.FSharp.Core.PrintfImpl.go@523-3[b,c,d](String fmt, Int32 len, FSharpFunc`2 outputChar, FSharpFunc`2 outa, b os, FSharpFunc`2 finalize, FSharpList`1 args, Int32 i)
       at Microsoft.FSharp.Core.PrintfImpl.run@521[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args)
       at Microsoft.FSharp.Core.PrintfImpl.capture@540[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args, Type ty, Int32 i)
       at Microsoft.FSharp.Core.PrintfImpl.gprintf[b,c,d,a](FSharpFunc`2 initialize, PrintfFormat`4 fmt)
       at FSI_0005.err@136-1.Invoke(String _arg1) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 138
       at Microsoft.FSharp.Control.AsyncBuilderImpl.args@753.Invoke(a a)
    actor://main-pc/err_1 pre-stop Status: Errored
    actor://main-pc/err_1 stopped Status: Shutdown

If you no longer require an actor to be supervised, then you can `Unwatch` the actor, repeating the OneForAll above
*)

let oneforallUnWatched = 
    Actor.supervisor (ActorPath.create "oneforall") Supervisor.OneForAll 
        [
            Actor.spawn (ActorPath.create "err_1") err;
            Actor.spawn (ActorPath.create "err_2") err
        ]

Actor.unwatch !*"err_2" 

!!"err_1" <-- "fail"

(**
We now see that one actor `err_1` has restarted

    Restarting (OneForAll actor://main-pc/err_1) due to error System.Exception: ERRRROROROR
       at FSI_0004.err@164-2.Invoke(String message) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 164
       at Microsoft.FSharp.Core.PrintfImpl.go@523-3[b,c,d](String fmt, Int32 len, FSharpFunc`2 outputChar, FSharpFunc`2 outa, b os, FSharpFunc`2 finalize, FSharpList`1 args, Int32 i)
       at Microsoft.FSharp.Core.PrintfImpl.run@521[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args)
       at Microsoft.FSharp.Core.PrintfImpl.capture@540[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args, Type ty, Int32 i)
       at Microsoft.FSharp.Core.PrintfImpl.gprintf[b,c,d,a](FSharpFunc`2 initialize, PrintfFormat`4 fmt)
       at FSI_0004.err@162-1.Invoke(String _arg1) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 164
       at Microsoft.FSharp.Control.AsyncBuilderImpl.args@753.Invoke(a a)
    actor://main-pc/err_1 pre-stop Status: Errored
    actor://main-pc/err_1 stopped Status: Shutdown
    actor://main-pc/err_1 pre-restart Status: Restarting
    actor://main-pc/err_1 re-started Status: OK

#Linking Actors

Linking an actor to another means that this actor will become a sibling of the other actor. This means that we can create relationships among actors
*)

let child i = 
    Actor.spawn (ActorPath.create <| sprintf "a/child_%d" i) 
         (fun actor -> async { 
                let! msg = actor.Receive()
                actor.Log.Info(sprintf "%A recieved %A" actor msg) 
              })

let parent = 
    Actor.spawnLinked (ActorPath.create "a/parent") 
            (fun actor -> async { 
                let! msg = actor.Receive()
                actor.Children <-* msg
              })
             <| List.init 5 (child)

parent <-- "Forward this to your children"

(**
This outputs

    actor://main-pc/a/child_1 recieved "Forward this to your children"
    actor://main-pc/a/child_3 recieved "Forward this to your children"
    actor://main-pc/a/child_2 recieved "Forward this to your children"
    actor://main-pc/a/child_4 recieved "Forward this to your children"
    actor://main-pc/a/child_0 recieved "Forward this to your children"

We can also unlink actors
*)

Actor.unlink !*"a/child_0" parent

parent <-- "Forward this to your children"

(**
This outputs

    actor://main-pc/a/child_1 recieved "Forward this to your children"
    actor://main-pc/a/child_3 recieved "Forward this to your children"
    actor://main-pc/a/child_2 recieved "Forward this to your children"
    actor://main-pc/a/child_4 recieved "Forward this to your children"
*)