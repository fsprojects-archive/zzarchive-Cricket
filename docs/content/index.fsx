(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
open System

(**
F# Actor
===================

Documentation

<div class="row">
  <div class="span1"></div>
  <div class="span6">
    <div class="well well-small" id="nuget">
      The F# Actor library can be <a href="https://nuget.org/packages/FSharp.Actor">installed from NuGet</a>:
      <pre>PM> Install-Package FSharp.Actor</pre>
    </div>
  </div>
  <div class="span1"></div>
</div>

Introducing F# Actor
========================

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

Samples & documentation
-----------------------

The library comes with comprehensible documentation. 
It can include a tutorials automatically generated from `*.fsx` files in [the content folder][content]. 
The API reference is automatically generated from Markdown comments in the library implementation.


 * [Tutorial](tutorial.html) contains a further explanation of this sample library.

 * [API Reference](reference/index.html) contains automatically generated documentation for all types, modules
   and functions in the library. This includes additional brief samples on using most of the
   functions.
 
Contributing and copyright
--------------------------

The project is hosted on [GitHub][gh] where you can [report issues][issues], fork 
the project and submit pull requests. If you're adding new public API, please also 
consider adding [samples][content] that can be turned into a documentation. You might
also want to read [library design notes][readme] to understand how it works.

The library is available under Public Domain license, which allows modification and 
redistribution for both commercial and non-commercial purposes. For more information see the 
[License file][license] in the GitHub repository. 

  [content]: https://github.com/colinbull/FSharp.Actor/tree/master/docs/content
  [gh]: https://github.com/colinbull/FSharp.Actor
  [issues]: https://github.com/colinbull/FSharp.Actor/issues
  [readme]: https://github.com/colinbull/FSharp.Actor/blob/master/README.md
  [license]: https://github.com/colinbull/FSharp.Actor/blob/master/LICENSE.txt
*)
