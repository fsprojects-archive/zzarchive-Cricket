namespace Cricket.Tests

open System
open System.Threading
open NUnit.Framework
open Cricket

[<TestFixture; Category("Unit")>]
type ``Given an Actor Configuration``() = 

    [<Test>]
    member t.``I can create an actor with a name``() = 
          let actor : ActorConfiguration<_> = 
              actor {
                name "TestActor"
              }
          Assert.AreEqual(ActorPath.ofString "TestActor", actor.Path)

    [<Test>]
    member t.``I can create an actor with a given path``() = 
           let actor : ActorConfiguration<_> = 
              actor {
                path (ActorPath.ofString "TestActor")
              }
           Assert.AreEqual(ActorPath.ofString "TestActor", actor.Path)      

[<TestFixture; Category("Unit")>]
type ``Given an Actor Lifecycle events get fired``() = 

    let actorBaseImpl() = 
        actor {
            name "TestActor"
            body (
                let rec loop() = 
                     messageHandler {
                        let! (msg : string) = Message.receive()
                        return! loop()
                     }
                loop())
        }

    let testBody msg actor = 
        let gate = new ManualResetEvent(false)
        let wasCalled = ref false
        let actor = 
            actor (async { 
                 wasCalled := true; 
                 gate.Set() |> ignore; 
                 return (); 
            }) 
            |> Actor.start

        actor <-- "Msg"
        actor <-- msg

        if gate.WaitOne()
        then Assert.True(!wasCalled)
        else Assert.Fail("Timeout")

    [<Test>]
    member t.``PreRestart gets fired``() =
           testBody SystemMessage.Restart (fun h ->  actor {
                  inherits (actorBaseImpl())
                  preRestart (fun () -> h)
              })

    [<Test>]
    member t.``PostRestart gets fired``() =
           testBody SystemMessage.Restart (fun h ->  actor {
                  inherits (actorBaseImpl())
                  postRestart (fun () -> h)
              })

    [<Test>]
    member t.``PreShutdown gets fired``() =
           testBody SystemMessage.Shutdown (fun h ->  actor {
                  inherits (actorBaseImpl())
                  preShutdown (fun () -> h)
              })

    [<Test>]
    member t.``PostShutdown gets fired``() =
           testBody SystemMessage.Shutdown (fun h ->  actor {
                  inherits (actorBaseImpl())
                  postShutdown (fun () -> h)
              })

    [<Test>]
    member t.``PreStartEvent gets fired``() =
           testBody "" (fun h ->  actor {
                  inherits (actorBaseImpl())
                  preStartup (fun () -> h)
              })