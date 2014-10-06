(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

ActorHost.Start()

let createActor localName = actor { name localName } |> Actor.spawn

[
        "/models/stats/mean"
        "/models/stats/stddev"
        "/models/agg/total/month"
        "/models/agg/total/year"
] |> List.iter (createActor >> ignore)

(**

Actor Lookup
------------

## ActorPaths

Actor Paths are a simply a string that identifies an actor or a group of actors. They look and work much like a web address. An example of an actor path
might be something like the following

    actor.tcp://actorHost1@192.168.1.90:8080/models/stats/average

This path is broken down into the following components. 


    {protocol}://{host name}@{protocol address}/{local path}
    
* **Protocol** - This defines which protocol actors should use to talk to each other. Local actors will simply use the `actor` protocol. When the actor
is a remote actor then this portion will be replaced with the scheme of the message that the transport was recieved on. For example if the message was received on
the `TcpActorProtocol` then this portion of the path will be set to `actor.tcp`

* **Host Name** - This identifies the host that the actor was created in. The host name can be set when calling [`ActorHost.Start(..)`](http://fsprojects.github.io/FSharp.Actor/reference/fsharp-actor-actorhost.html)

* **Protocol Address** - Typically you will simply let the framework resolve this for you. As the exact structure of this will depend on the underlying transport. In the example above 
we are using the `actor.tcp` transport so the IP address an port are required for this transport; However another example maybe a message queue based protocol in which case this may define the topic or queue name.

* **Local Path** - This is the name that was given to the actor when it was defined.

At first look Actor paths can seem quite verbose, but in truth it is a rare occasion when we need to specify a fully qualified path like the one above. Typically we can just get away with specifying the 
local path portion, for example

    /models/stats/average

This path is a pointer to **ALL** actors on **ALL** nodes with that name. For example if there where 5 nodes all with an actor registered `/models/stats/average` then resolving this will lead to 5 fully qualified
actor paths
    
    [
        actor.tcp://actorhost1@192.168.1.90:8080/models/stats/average
        actor.tcp://actorhost2@192.168.1.90:8081/models/stats/average
        actor.tcp://actorhost3@192.168.1.90:8082/models/stats/average
        actor.tcp://actorhost4@192.168.1.90:8083/models/stats/average
        actor.tcp://actorhost5@192.168.1.90:8084/models/stats/average
    ]

This is all fine, but what if there are several actors all under a different name, is it possible to reference them all with a single path? Answer: Yes, but the local path needs some structure. When the 
local path of the actor is defined, it is defined like any path. This path is used to build a tree. 
    
    [lang=text]
    models
       |
       |--- stats
              |
              |--- average

so to be able to match different types of actors we need a common root. Given, 

    [
        /models/stats/mean
        /models/stats/stddev
        /models/agg/total/month
        /models/agg/total/year
    ]

This will build the following tree
    
    [lang=text]
    models
       |
       |--- stats
       |     |
       |     |--- mean
       |     |--- stdev
       |
       |--- agg
             |
             |--- total
                    |
                    |--- month
                    |--- year

Now to access all of the actors in sub trees we can use wildcards. 

Using `/models/*` will return

    [
        /models/stats/mean
        /models/stats/stddev
        /models/agg/total/month
        /models/agg/total/year
    ]

Using `/models/stats/*` will return

    [
        /models/stats/mean
        /models/stats/stddev
    ]

## Actor References and Actor Selections

Actor References relate to an actor path. When a actor path is resolved, the resolution process returns one or more ActorRef's. Rarely will you interact directly with an
`ActorRef`. When an `ActorPath` is resolved we get and `ActorSelection` which is an abstraction over a collection of ActorRefs.

## Resolving Actor Paths

To resolve an actor path, we have several operators at our disposal `!!`, `!~` or the named equivalents `resolve`, `resolveLazy`. These operators are able to resolve many types to an `ActorSelection`
a few examples are:
*)

let selection1 = resolve "/models/stats/mean"
(** which gives us *)
(*** include-value: selection1 ***)

let selection2 = !!"/models/stats/mean"
(** which gives us *)
(*** include-value: selection2 ***)

let selection3 = !!["/models/agg/total/*"; "/models/stats/mean"]
(** which gives us *)
(*** include-value: selection3 ***)

(**
for a complete list see [here](https://github.com/fsprojects/FSharp.Actor/blob/master/src/FSharp.Actor/ActorSelection.fs#L12).

###Actor Selection combinators

The library also provides a set of functions over `ActorSelection` see [here](http://fsprojects.github.io/FSharp.Actor/reference/fsharp-actor-actorselectionmodule.html). These 
all you to take an `ActorSelection` create a new actor selection. For example if I wanted to exclude `selection2` from `selection3` I could do the following   
*)

let selection4 = ActorSelection.exclude selection2 selection3
(** which gives us *)
(*** include-value: selection4 ***)