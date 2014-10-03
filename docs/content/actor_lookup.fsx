(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

(**

Actor Lookup
------------

An important property of any actor system is the ability to send a message to an actor without having a tight coupling to that actor.
FSharp.Actor allows this by providing a simple abstract called `ActorSelection` an actor selection is simply a list of `ActorRef`.
*)