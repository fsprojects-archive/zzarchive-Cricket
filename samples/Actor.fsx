#load "Dependencies.fsx"
open FSharp.Actor

(**
#Basic Actors
*)

let multiplication = 
    (fun (actor:IActor<_>) ->
        let rec loop() =
            async {
                let! ((a,b), sender) = actor.Receive()
                let result = a * b
                do printfn "%A: %d * %d = %d" actor.Path a b result
                return! loop()
            }
        loop()
    )

let addition = 
    (fun (actor:IActor<_>) ->
        let rec loop() =
            async {
                let! ((a,b), sender) = actor.Receive()
                let result = a + b
                do printfn "%A: %d + %d = %d" actor.Path a b result
                return! loop()
            }
        loop()
    )

let calculator = 
    [
       Actor.spawn (Actor.Options.Create("calculator/addition")) addition
       Actor.spawn (Actor.Options.Create("calculator/multiplication")) multiplication
    ]

(**
The above code creates two actors `calcualtor/addition` and `calculator/multiplication`

    calculator/addition pre-start Status: Shutdown "Initial Startup"
    calculator/addition started Status: Running "Initial Startup"
    calculator/multiplication pre-start Status: Shutdown "Initial Startup"
    calculator/multiplication started Status: Running "Initial Startup"
    
    val multiplication : actor:FSharp.Actor.Actor<int * int> -> Async<unit>
    val addition : actor:FSharp.Actor.Actor<int * int> -> Async<unit>
    val calculator : FSharp.Actor.ActorRef list =
      [calculator/addition; calculator/multiplication]

We can see that the actors state transitions are logged.

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
"calculator" ?<-- (5,2)

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

calculator.[1] <!- (Shutdown("Cause I want to"))

(** or *)

"calculator/addition" ?<!- (Shutdown("Cause I want to"))

(**
Sending now sending any message to the actor will result in an exception 

    System.InvalidOperationException: Actor (actor://main-pc/calculator/addition) could not handle message, State: Shutdown
*)


(**
#Changing the behaviour of actors

You can change the behaviour of actors at runtime. This achieved through mutually recursive functions
*)

let rec schizoPing = 
    (fun (actor:IActor<_>) ->
        let log = (actor :?> Actor.T<_>).Log
        let rec ping() = 
            async {
                let! (msg,_) = actor.Receive()
                log.Info(sprintf "(%A): %A ping" actor msg, None)
                return! pong()
            }
        and pong() =
            async {
                let! (msg,_) = actor.Receive()
                log.Info(sprintf "(%A): %A pong" actor msg, None)
                return! ping()
            }
        ping()
    )
        

let schizo = Actor.spawn (Actor.Options.Create("schizo")) schizoPing 

!!"schizo" <-- "Hello"

(**

Sending two messages to the 'schizo' actor results in

    (schizo): "Hello" ping

followed by

    (schizo): "Hello" pong


#Supervising Actors

Actors can supervise other actors, if we define an actor loop that fails on a given message
*)

let err = 
        (fun (actor:IActor<string>) ->
            let rec loop() =
                async {
                    let! (msg,_) = actor.Receive()
                    if msg <> "fail"
                    then printfn "%s" msg
                    else failwithf "ERRRROROROR"
                    return! loop()
                }
            loop()
        )

(**
then a supervisor will allow the actor to restart or terminate depending on the particular strategy that is in place

##Strategies

A supervisor strategy allows you to define the restart semantics for the actors it is watching

###OneForOne
    
A supervisor will only restart the actor that has errored
*)

let oneforone = 
    Supervisor.spawn 
        <| Supervisor.Options.Create(actorOptions = Actor.Options.Create("OneForOne"))
    |> Supervisor.superviseAll [Actor.spawn (Actor.Options.Create("err_0")) err]

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
    Supervisor.spawn 
        <| Supervisor.Options.Create(
                    strategy = Supervisor.Strategy.OneForAll,
                    actorOptions = Actor.Options.Create("OneForAll")
           )
    |> Supervisor.superviseAll
        [
            Actor.spawn (Actor.Options.Create("err_1")) err;
            Actor.spawn (Actor.Options.Create("err_2")) err
        ]
"err_1" ?<-- "Boo"
"err_2" ?<-- "fail"

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
    Supervisor.spawn 
        <| Supervisor.Options.Create(
                    strategy = Supervisor.Strategy.AlwaysFail,
                    actorOptions = Actor.Options.Create("Fail")
           )
    |> Supervisor.superviseAll
        [
            Actor.spawn (Actor.Options.Create("err_3")) err;
            Actor.spawn (Actor.Options.Create("err_4")) err
        ]

!!"err_3" <-- "fail"

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

let oneforallunwatch = 
    Supervisor.spawn 
        <| Supervisor.Options.Create(
                    strategy = Supervisor.Strategy.OneForAll,
                    actorOptions = Actor.Options.Create("OneForAll")
           )
    |> Supervisor.superviseAll
        [
            Actor.spawn (Actor.Options.Create("err_5")) err;
            Actor.spawn (Actor.Options.Create("err_6")) err
        ]

Actor.unwatch !*"err_6" 

!!"err_5" <-- "fail"

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
    Actor.spawn (Actor.Options.Create(sprintf "a/child_%d" i)) 
         (fun actor ->
             let log = (actor :?> Actor.T<_>).Log 
             let rec loop() =
                async { 
                   let! msg = actor.Receive()
                   log.Info(sprintf "%A recieved %A" actor msg, None) 
                   return! loop()
                }
             loop()
         )

let parent = 
    Actor.spawnLinked (Actor.Options.Create "a/parent") (List.init 5 (child))
            (fun actor -> 
                let rec loop() =
                  async { 
                      let! msg = actor.Receive()
                      actor.Children <-* msg
                      return! loop()
                  }
                loop()    
            ) 

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