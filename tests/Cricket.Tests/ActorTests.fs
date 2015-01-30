namespace Cricket.Tests

open System
open System.Threading
open NUnit.Framework
open FsUnit
open Cricket

[<TestFixture; Category("Unit")>]
type ``Given an Actor Configuration``() = 

    [<Test>]
    member t.``I can create an actor with a name``() = 
          let actor : ActorConfiguration<_> = 
              actor {
                name "TestActor"
              }
          actor.Path |> should equal (ActorPath.ofString "TestActor")

    [<Test>]
    member t.``I can create an actor with a given path``() = 
           let actor : ActorConfiguration<_> = 
              actor {
                path (ActorPath.ofString "TestActor")
              }
           actor.Path |> should equal (ActorPath.ofString "TestActor")           

[<TestFixture; Category("Unit")>]
type ``Given an Actor Lifecycle events get fired``() = 

    let actorBaseImpl = 
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

    [<Test>]
    member t.``PreStartEvent gets fired on first start``() =
          let gate = new ManualResetEvent(false)
          let wasCalled = ref false
          let actor = 
              actor {
                  inherits actorBaseImpl
                  preStartup (fun () -> async { wasCalled := true; gate.Set() |> ignore; return (); })
              } |> Actor.start

          actor <-- "Msg"

          if gate.WaitOne()
          then !wasCalled |> should equal true
          else Assert.Fail("Timeout")

