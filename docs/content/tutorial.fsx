(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

(**

Hello World Example
===================

The example below is essentially the `Hello World` of actors. It is a 
simple actor called `greeter` which responds to messages of type `Say`. Given a message the actor will write
a response to the debug log then wait for another message. 
*)

ActorHost.Start()

(**
Before we create any actor we must start the actor host for 
out actor to live in. There is only one actor host per process.

Next we create a type to represent the messages that we want to send. In this example we create a three
legged union to represent our messages, but this could just of easily been a class, record or any other 
type really. Typically though unions are a good fit for defining message types. 
*)

type Say =
    | Hello
    | HelloWorld
    | Name of string

(**
Now we have our message type, its is time to create our actor. To do this we can use the
`actor {}` syntax. The syntax allows for various different properties to be set (see [here](reference/fsharp-actor-actorconfiguration-1.html)),
but name and messageHandler are the minimum required to get a functioning actor. The messageHandler is simply a function that provides the behaviour 
of the actor. It is of type `ActorCell<'a> -> Async<unit>` where the [ActorCell<'a>](reference/fsharp-actor-actorcell-1.html) 
type holds the context for this actor. To wait for a message we simply call receive, this will block this thread until a message 
arrives at the actor. Once it arrives, the function will resume and process the message. Once we have finished defininf our actor
configuration we can spawn the actor. 

*)

let greeter = 
    actor {
        name "greeter"
        body (
            let rec loop() = messageHandler {
                let! msg = Message.receive None //Wait for a message

                match msg with
                | Hello ->  printfn "Hello" //Handle Hello leg
                | HelloWorld -> printfn "Hello World" //Handle HelloWorld leg
                | Name name -> printfn "Hello, %s" name //Handle Name leg

                return! loop() //Recursively loop

            }
            loop())
    } |> Actor.spawn

(**
Once the actor is spawned it is now ready to except messages. To post a message to an actor we can use the `<--` operator, like this.
*)

greeter <-- Name("from F# Actor")

(**
This requires you to have a direct reference to the actor, which is not always practical. It would be nice to be able to resolve actors by name
at runtime and in fact the framework lets you do this using the `!!` operator. 
*)

let resolvedGreeter = !!"greeter"

(**
The `!!` operator returns an [actorSelection](reference/fsharp-actor-actorselection-0.html) type, which can represent one or many actors that 
match the path you gave (for more on actor lookup see [here](actor_lookup.html)). once we have an `actorSelection` we can use it exactly 
like the direct reference. 
*)

resolvedGreeter <-- Hello

(**
Or we can inline all of this
*)

"greeter" <-- Hello

(**

System Messages
---------------

Great.. so at this point we have an actor and can send messages to it. But we have no obvious way of shutting down this actor once we are done with it. Now we could
add an extra leg to our message type `Stop` for example that would cause the message loop to not recursively return but instead simply exit. And in some domains this 
will be desirable. But it is worth noting that the F# Actor Framework, provides a set of [System Messages](reference/fsharp-actor-systemmessage.html) that allow you to control
certain framework level items to do with your actor, like shutting down, restarting or several other tasks to do with supervisors. 

Here we will just focus on shutting down the actor, since this is built into the framework all we have to do is send it the `Shutdown` or `Restart` message
*)

greeter <-- Restart

greeter <-- Shutdown

(**
Thats about it for the basics, for more indepth examples have a look at the links below. 

More in depth examples
----------------------
 * [Ping Pong](pingpong.html) - A slightly more advanced example, showing how two actors can communicate with each other within the same process.
 * [Ping Pong - Remoting Version](remoting.html) - Same as the in process Ping Pong example, however the actors are in seperate processes.
*)


