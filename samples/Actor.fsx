#load "Dependencies.fsx"
open FSharp.Actor
open FSharp.Actor.DSL

(**
#Actors

An actor is a computational entity that, in response to a message it receives, can concurrently:

    * send a finite number of messages to other actors.
    * create a finite number of new actors.
    * designate the behavior to be used for the next message it receives.

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
##Changing the behaviour of actors

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

##Linking Actors

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