[![Issue Stats](http://issuestats.com/github/fsprojects/Cricket/badge/issue)](http://issuestats.com/github/fsprojects/Cricket)
[![Issue Stats](http://issuestats.com/github/fsprojects/Cricket/badge/pr)](http://issuestats.com/github/fsprojects/Cricket)

# Cricket [![NuGet Status](http://img.shields.io/nuget/v/Cricket.svg?style=flat)](https://www.nuget.org/packages/Cricket/)

Cricket is an actor library. The actor programming model is inherently concurrent, an actor is a primitive that wraps a computation, the computation is ran by sending messages to the actor.
The actor can then respond to the receipt of the message by executing one or more of the following actions (possibly concurrently),

 * Create a number of new actors
 * Send a another message to other actors
 * Change the behaviour to be used upon receipt of the next message.

Currently there are a couple of actor libraries on the .NET platform
    
* [AkkaDotNet](https://github.com/akkadotnet/akka.net) - This is a port of the scala based actor framework Akka. This is written in C# but does have an F# API.
* [Orleans](http://research.microsoft.com/en-us/projects/orleans/) - This is a Microsoft research project aiming to simplfy the development of scalable cloud based services, with a high level of abstraction over the actor programming model.  
* [F# Core](http://msdn.microsoft.com/en-us/library/ee370357.aspx) - The FSharp.Core library actually has its own actor implementation in the form of the `MailboxProcessor<'T>` type. 

Cricket in no way aims to be a clone of either of these however it does draw on the ideas in all of the above frameworks as well as Erlang and OTP. Cricket aims to be as simple and safe to use as possible hopefully
making it very difficult for you to shoot or self in the foot.

## Building

- Simply build Cricket.sln in Visual Studio, Mono Develop, or Xamarin Studio. You can also use the FAKE script:

  * Windows: Run *build.cmd* 
    * [![AppVeyor Build status](https://ci.appveyor.com/api/projects/status/4adhvsdt0sktqo95/branch/master)](https://ci.appveyor.com/project/colinbull/fsharp-actor/branch/master)
  * Mono: Run *build.sh*
    * [![Travis Build Status](https://travis-ci.org/fsprojects/Cricket.svg?branch=master)](https://travis-ci.org/fsprojects/Cricket)

## Simple Example


	#r "Cricket.dll"
	open Cricket
	
	ActorHost.Start()
	
	type Say =
	    | Hello
	    | HelloWorld
	    | Name of string
	
	let greeter = 
	    actor {
	        name "greeter"
	        body (
	            let rec loop() = messageHandler {
	                let! msg = Message.receive()
	
	                match msg with
	                | Hello ->  printfn "Hello"
	                | HelloWorld -> printfn "Hello World"
	                | Name name -> printfn "Hello, %s" name
	                return! loop()
	
	            }
	            loop())
	    } |> Actor.spawn
	
	greeter <-- Name("from F# Actor") 


## Maintainer(s)

- [@colinbull](https://github.com/colinbull)

The default maintainer account for projects under "fsprojects" is [@fsprojectsgit](https://github.com/fsprojectsgit) - F# Community Project Incubation Space (repo management)
