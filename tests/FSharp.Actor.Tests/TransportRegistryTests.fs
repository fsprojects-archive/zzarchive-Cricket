namespace FSharp.Actor.Tests

open NUnit.Framework
open FsUnit
open FSharp.Actor
open FSharp.Actor.Types

[<TestFixture; Category("Unit")>]
type ``Given a Transport Registry``() = 
    
    let remoteActor (address:ActorPath) = 
        Actor.deadLetter address.AbsoluteUri :> IActor

    let mockTransport = 
        { new ITransport with
            member x.Scheme with get() = "mock"
            member x.CreateRemoteActor(address) = Actor.deadLetter address.AbsoluteUri :> IActor
            member x.Send(_,_,_) =()
            member x.SendSystemMessage(_,_,_) =()
        }

    [<SetUp>]
    member t.setup() =
        Registry.Transport.clear()

    [<Test>]
    member t.``I can register a transport``() =
        Registry.Transport.register mockTransport
        let actual = Registry.Transport.all()
        let expected = [mockTransport]
        actual |> should equal expected

    [<Test>]
    member t.``I can find a registered transport``() = 
        Registry.Transport.register mockTransport
        let actual = Registry.Transport.tryFind "mock"
        let expected = Some mockTransport
        actual |> should equal expected

    [<Test>]
    member t.``I can create a remote actor for a given address and transport``() =
        let address = Path.create "actor://127.0.0.1:8080/mock/transport"
        Registry.Transport.register mockTransport
        let actual = Registry.Transport.tryFindActorsForTransport "mock" address
        let expected = [remoteActor address]
        actual |> should equal expected

    [<Test>]
    member t.``I can remove a transport``() = 
        Registry.Transport.register mockTransport
        Registry.Transport.remove "mock"
        let actual = Registry.Transport.all()
        let expected = [] 
        actual |> should equal expected
