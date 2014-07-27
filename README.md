Fsharp.Actor
============

F# Actor is an actor library. The actor programming model is inherently concurrent, an actor is a primitive that wraps a computation, the computation is ran by sending messages to the actor.
The actor can then respond to the reciept of the message by executing one or more of the following actions (possibly concurrently),

 * Create a number of new actors
 * Send a another message to other actors
 * Change the behaviour to be used upon reciept of the next message.

Currently there are a couple of actor libraries on the .NET platform
    
    * [AkkaDotNet](https://github.com/akkadotnet/akka.net) - This is a port of the scala based actor framework Akka. This is written in C# but does have an F# API.
    * [Orleans](http://research.microsoft.com/en-us/projects/orleans/) - This is a Microsoft research project aiming to simplfy the development of scalable cloud based services, with a high level of abstraction over the actor programming model.  
    * [F# Core](http://msdn.microsoft.com/en-us/library/ee370357.aspx) - The FSharp.Core library actually has its own actor implementation in the form of the `MailboxProcessor<'T>` type. 

F# Actor in no way aims to be a clone of either of these however it does draw on the ideas in all of the above frameworks as well as Erlang and OTP. Instead F# actor aims to be as simple and safe to use as possible hopefully
making it very difficult for you to shoot or self in the foot.

Simple Example
--------------

	#r "FSharp.Actor.dll"
	open FSharp.Actor
	
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
	                let! msg = actor.Receive()
	                match msg.Message with
	                | Hello ->  actor.Logger.Debug("Hello")
	                | HelloWorld -> actor.Logger.Debug("Hello World")
	                | Name name -> actor.Logger.Debug(sprintf "Hello, %s" name)
	                return! loop()
	            }
	            loop())
	    } |> system.SpawnActor
	
	greeter <-- Name("from F# Actor") 