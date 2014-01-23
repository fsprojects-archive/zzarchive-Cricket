(*** hide ***)
#load "Dependencies.fsx"
open FSharp.Actor

(**
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

// This yields
// [fsi:Restarting (OneForOne: actor://main-pc/err_0) due to error System.Exception: ERRRROROROR]
// [fsi:    at FSI_0012.err@134-2.Invoke(String message) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 134]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.go@523-3[b,c,d](String fmt, Int32 len, FSharpFunc`2 outputChar, FSharpFunc`2 outa, b os, FSharpFunc`2 finalize, FSharpList`1 args, Int32 i)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.run@521[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.capture@540[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args, Type ty, Int32 i)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.gprintf[b,c,d,a](FSharpFunc`2 initialize, PrintfFormat`4 fmt)]
// [fsi:    at FSI_0012.err@132-1.Invoke(String _arg1) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 134]
// [fsi:    at Microsoft.FSharp.Control.AsyncBuilderImpl.args@753.Invoke(a a)]
// [fsi:actor://main-pc/err_0 pre-stop Status: Errored]
// [fsi:actor://main-pc/err_0 stopped Status: Shutdown]
// [fsi:actor://main-pc/err_0 pre-restart Status: Restarting]
// [fsi:actor://main-pc/err_0 re-started Status: OK]

(**
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


// This yields
// [fsi:Restarting (OneForAll actor://main-pc/err_1) due to error System.Exception: ERRRROROROR]
// [fsi:    at FSI_0004.err@134-2.Invoke(String message) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 134]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.go@523-3[b,c,d](String fmt, Int32 len, FSharpFunc`2 outputChar, FSharpFunc`2 outa, b os, FSharpFunc`2 finalize, FSharpList`1 args, Int32 i)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.run@521[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.capture@540[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args, Type ty, Int32 i)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.gprintf[b,c,d,a](FSharpFunc`2 initialize, PrintfFormat`4 fmt)]
// [fsi:    at FSI_0004.err@132-1.Invoke(String _arg1) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 134]
// [fsi:    at Microsoft.FSharp.Control.AsyncBuilderImpl.args@753.Invoke(a a)]
// [fsi:actor://main-pc/err_2 pre-stop Status: OK]
// [fsi:actor://main-pc/err_2 stopped Status: Shutdown]
// [fsi:actor://main-pc/err_2 pre-restart Status: Restarting]
// [fsi:actor://main-pc/err_2 re-started Status: OK]
// [fsi:actor://main-pc/err_1 pre-stop Status: Errored]
// [fsi:actor://main-pc/err_1 stopped Status: Shutdown]
// [fsi:actor://main-pc/err_1 pre-restart Status: Restarting]
// [fsi:actor://main-pc/err_1 re-started Status: OK]

(**
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

// This yields
// [fsi:Terminating (AlwaysTerminate: actor://main-pc/err_1) due to error System.Exception: ERRRROROROR]
// [fsi:    at FSI_0005.err@138-2.Invoke(String message) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 138]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.go@523-3[b,c,d](String fmt, Int32 len, FSharpFunc`2 outputChar, FSharpFunc`2 outa, b os, FSharpFunc`2 finalize, FSharpList`1 args, Int32 i)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.run@521[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.capture@540[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args, Type ty, Int32 i)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.gprintf[b,c,d,a](FSharpFunc`2 initialize, PrintfFormat`4 fmt)]
// [fsi:    at FSI_0005.err@136-1.Invoke(String _arg1) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 138]
// [fsi:    at Microsoft.FSharp.Control.AsyncBuilderImpl.args@753.Invoke(a a)]
// [fsi:actor://main-pc/err_1 pre-stop Status: Errored]
// [fsi:actor://main-pc/err_1 stopped Status: Shutdown]

(**
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

// We now see that one actor `err_1` has restarted
// [fsi:Restarting (OneForAll actor://main-pc/err_1) due to error System.Exception: ERRRROROROR]
// [fsi:    at FSI_0004.err@164-2.Invoke(String message) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 164]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.go@523-3[b,c,d](String fmt, Int32 len, FSharpFunc`2 outputChar, FSharpFunc`2 outa, b os, FSharpFunc`2 finalize, FSharpList`1 args, Int32 i)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.run@521[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.capture@540[b,c,d](FSharpFunc`2 initialize, String fmt, Int32 len, FSharpList`1 args, Type ty, Int32 i)]
// [fsi:    at Microsoft.FSharp.Core.PrintfImpl.gprintf[b,c,d,a](FSharpFunc`2 initialize, PrintfFormat`4 fmt)]
// [fsi:    at FSI_0004.err@162-1.Invoke(String _arg1) in D:\Appdev\fsharp.actor\samples\Actor.fsx:line 164]
// [fsi:    at Microsoft.FSharp.Control.AsyncBuilderImpl.args@753.Invoke(a a)]
// [fsi:actor://main-pc/err_1 pre-stop Status: Errored]
// [fsi:actor://main-pc/err_1 stopped Status: Shutdown]
// [fsi:actor://main-pc/err_1 pre-restart Status: Restarting]
// [fsi:actor://main-pc/err_1 re-started Status: OK]
