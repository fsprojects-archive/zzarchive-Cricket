(*** hide ***)
#load "Dependencies.fsx"
open FSharp.Actor
<<<<<<< HEAD
=======
open FSharp.Actor.DSL
>>>>>>> c57948e0df72aae1b7114f96cc913f73cd0d069a

(**
#Supervising Actors

Actors can supervise other actors, if we define an actor loop that fails on a given message
*)

let err = 
<<<<<<< HEAD
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
=======
    (fun (actor:Actor<string>) ->
        async {
            let! msg = actor.Receive()
            if msg <> "fail"
            then printfn "%s" msg
            else failwithf "ERRRROROROR"
        }
    )
>>>>>>> c57948e0df72aae1b7114f96cc913f73cd0d069a

(**
then a supervisor will allow the actor to restart or terminate depending on the particular strategy that is in place

##Strategies

A supervisor strategy allows you to define the restart semantics for the actors it is watching

###OneForOne
    
A supervisor will only restart the actor that has errored
*)

let oneforone = 
<<<<<<< HEAD
    Supervisor.spawn 
        <| Supervisor.Options.Create(actorOptions = Actor.Options.Create("OneForOne"))
    |> Supervisor.superviseAll [Actor.spawn (Actor.Options.Create("err_0")) err]
=======
    Actor.supervisor (ActorPath.create "oneforone") Supervisor.OneForOne [Actor.spawn (ActorPath.create "err_0") err]
>>>>>>> c57948e0df72aae1b7114f96cc913f73cd0d069a

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
<<<<<<< HEAD
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
=======
    Actor.supervisor (ActorPath.create "oneforall") Supervisor.OneForAll 
        [
            Actor.spawn (ActorPath.create "err_1") err;
            Actor.spawn (ActorPath.create "err_2") err
        ]

!!"err_1" <-- "fail"
>>>>>>> c57948e0df72aae1b7114f96cc913f73cd0d069a

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
<<<<<<< HEAD
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
=======
    Actor.supervisor (ActorPath.create "fail") Supervisor.Fail 
        [
            Actor.spawn (ActorPath.create "err_1") err;
            Actor.spawn (ActorPath.create "err_2") err
        ]

!!"err_1" <-- "fail"
>>>>>>> c57948e0df72aae1b7114f96cc913f73cd0d069a

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

<<<<<<< HEAD
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
=======
let oneforallUnWatched = 
    Actor.supervisor (ActorPath.create "oneforall") Supervisor.OneForAll 
        [
            Actor.spawn (ActorPath.create "err_1") err;
            Actor.spawn (ActorPath.create "err_2") err
        ]

Actor.unwatch !*"err_2" 

!!"err_1" <-- "fail"
>>>>>>> c57948e0df72aae1b7114f96cc913f73cd0d069a

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

<<<<<<< HEAD
=======
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
>>>>>>> c57948e0df72aae1b7114f96cc913f73cd0d069a
*)