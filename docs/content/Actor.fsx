(*** hide ***)
#load "Dependencies.fsx"
open FSharp.Actor

(**
#Actors

An actor is a computational entity that, in response to a message it receives, can concurrently:

* Send a finite number of messages to other actors.
* Create a finite number of new actors.
* Designate the behavior to be used for the next message it receives.

_as defined by wikipedia_
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

// The above code creates two actors `calcualtor/addition` and `calculator/multiplication`
// [fsi:calculator/addition pre-start Status: Shutdown "Initial Startup"]
// [fsi:calculator/addition started Status: Running "Initial Startup"]
// [fsi:calculator/multiplication pre-start Status: Shutdown "Initial Startup"]
// [fsi:calculator/multiplication started Status: Running "Initial Startup"]
// [fsi:    ]
// [fsi:val multiplication : actor:FSharp.Actor.Actor<int * int> -> Async<unit>]
// [fsi:val addition : actor:FSharp.Actor.Actor<int * int> -> Async<unit>]
// [fsi:val calculator : FSharp.Actor.ActorRef list =]
// [fsi:    [calculator/addition; calculator/multiplication]]

(**
We can see that the actors state transitions are logged.

Once we have created our actors we can be looked up by their path
*)

"calculator/addition" ?<-- (5,2)
"calculator/multiplication" ?<-- (5,2)

// Sending both of these messages yields
// [fsi:actor://main-pc/calculator/addition: 5 + 2 = 7]
// [fsi:actor://main-pc/calculator/multiplication: 5 * 2 = 10]

(**
We can also send messages directly to actors if we have their `ActorRef`
*)

calculator.[0] <-- (5,2)

// This also yields 
// [fsi:actor://main-pc/calculator/addition: 5 + 2 = 7]

(**
Or we could have broadcast to all of the actors in that collection
*)

calculator <-* (5,2)

// This also yields 
// [fsi:actor://main-pc/calculator/addition: 5 + 2 = 7]
// [fsi:actor://main-pc/calculator/multiplication: 5 * 2 = 10]

(**
We can also resolve _systems_ of actors.
*)

"calculator" ?<-- (5,2)

// This also yields 
// [fsi:actor://main-pc/calculator/addition: 5 + 2 = 7]
// [fsi:actor://main-pc/calculator/multiplication: 5 * 2 = 10]

(**
However this actor wont be found because it does not exist
*)

"calculator/addition/foo" ?<-- (5,2)

// resulting in a `KeyNotFoundException`
// [fsi:System.Collections.Generic.KeyNotFoundException: Could not find actor calculator/addition/foo]

(**
We can also kill actors 
*)

calculator.[1] <!- (Shutdown("Cause I want to"))
// or
"calculator/addition" ?<!- (Shutdown("Cause I want to"))

// Sending now sending any message to the actor will result in an exception 
// [fsi:System.InvalidOperationException: Actor (actor://main-pc/calculator/addition) could not handle message, State: Shutdown]

(**
##Changing the behaviour of actors

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

// Sending two messages to the 'schizo' actor results:
// [fsi:(schizo): "Hello" ping]
// [fsi:(schizo): "Hello" pong]

(**
##Linking Actors

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

// This outputs
// [fsi:actor://main-pc/a/child_1 recieved "Forward this to your children"]
// [fsi:actor://main-pc/a/child_3 recieved "Forward this to your children"]
// [fsi:actor://main-pc/a/child_2 recieved "Forward this to your children"]
// [fsi:actor://main-pc/a/child_4 recieved "Forward this to your children"]
// [fsi:actor://main-pc/a/child_0 recieved "Forward this to your children"]

(**
We can also unlink actors
*)

Actor.unlink !* "a/child_0" parent

parent <-- "Forward this to your children"

// This outputs
// [fsi:actor://main-pc/a/child_1 recieved "Forward this to your children"]
// [fsi:actor://main-pc/a/child_3 recieved "Forward this to your children"]
// [fsi:actor://main-pc/a/child_2 recieved "Forward this to your children"]
// [fsi:actor://main-pc/a/child_4 recieved "Forward this to your children"]
