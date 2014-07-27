(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

(**

Hello World Example
===================

The example below is essentially the `Hello World` of actors.
Here we create a discriminated union to represent the type of messages that we want the actor to process. 
Next we create an actor called `/greeter` to handle the messages. Then finally we use `<--` to send the message
so the actor can process it. 
*)

ActorHost.Start()
let system = ActorHost.CreateSystem("greeterSystem")

type Say =
    | Hello
    | HelloWorld
    | Name of string

let greeter = 
    actor {
        name "greeter"
        messageHandler (fun actor ->
            let rec loop() = async {
                let! msg = actor.Receive() //Wait for a message

                match msg.Message with
                | Hello ->  actor.Logger.Debug("Hello") //Handle Hello leg
                | HelloWorld -> actor.Logger.Debug("Hello World") //Handle HelloWorld leg
                | Name name -> actor.Logger.Debug(sprintf "Hello, %s" name) //Handle Name leg
                return! loop() //Recursively loop

            }
            loop())
    } |> system.SpawnActor

greeter <-- Name("from F# Actor")

(**
In the above example we have created simple actor called `greeter` which responds to messages of type `Say`. 
*)

(**
More in depth examples
----------------------
    *[Ping Pong](pingpong.html) - A slightly more advanced example, showing how two actors can communicate with each other.
*)


