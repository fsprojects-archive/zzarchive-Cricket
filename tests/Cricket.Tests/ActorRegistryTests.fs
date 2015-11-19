namespace Cricket.Tests

open System
open NUnit.Framework
open Cricket

type NullActor(path) =
    interface IActor with
        member x.Path with get() = ActorPath.ofString path
        member x.Post(msg) = raise <| NotImplementedException()
        member x.Dispose() = ()

[<TestFixture; Category("Unit")>]
type ``With Local Registry``() = 

    let actor(path) = ActorRef (new NullActor(path))
    
    [<Test>]
    member t.``I can register and resolve an actor with full URI``() = 
        let registry = new InMemoryActorRegistry() :> ActorRegistry
        let actor = actor "actor.transport://node1@localhost:6667/test/actor"
        registry.Register actor
        let result = registry.Resolve(ActorPath.ofString "actor.transport://node1@localhost:6667/test/actor")
        Assert.AreEqual([actor], result)

    [<Test>]
    member t.``I can register and resolve an actor with only path``() = 
        let registry = new InMemoryActorRegistry() :> ActorRegistry
        let actor = actor "actor.transport://node1@localhost:6667/test/actor"
        registry.Register actor
        let result = registry.Resolve(ActorPath.ofString "test/actor")
        Assert.AreEqual([actor], result)