namespace FSharp.Actor.Tests

open NUnit.Framework
open FsUnit
open FSharp.Actor

[<TestFixture; Category("Unit")>]
type ``Given a Actor Registry``() = 
    
    [<SetUp>]
    member t.setup() =
        Registry.Actor.clear()

    [<Test>]
    member t.``I can register an actor``() =
        let actor = Actor.deadLetter "test"
        Registry.Actor.register actor |> ignore

        let actual = Registry.Actor.all() 
        let expected = [actor]
        actual |> should equal expected

    [<Test>]
    member t.``I can unregister an actor``() =
        let actor = Actor.deadLetter "test"
        Registry.Actor.register actor |> ignore
        Registry.Actor.remove actor.Path

        let actual = Registry.Actor.all() 
        let expected = []
        actual |> should equal expected

    [<Test>]
    member t.``I can find an actor by path``() =
        let actors = List.init 5 (fun i -> Actor.deadLetter (sprintf "a/%d" i))
        actors |> List.iter (Registry.Actor.register >> ignore)
        let actual = Registry.Actor.find (Path.create "a/4")
        let expected = actors.[4]
        actual |> should equal expected

    [<Test>]
    member t.``I can find an actor under a path``() =
        let actors = List.init 5 (fun i -> Actor.deadLetter (sprintf "a/%d" i))
        actors |> List.iter (Registry.Actor.register >> ignore)
        let actual = Registry.Actor.findUnderPath (Path.create "a/")
        let expected = actors
        actual |> should equal expected

    [<Test>]
    member t.``/ returns all of the actors``() =
        let actors = List.init 5 (fun i -> Actor.deadLetter (sprintf "a/%d" i))
        actors |> List.iter (Registry.Actor.register >> ignore)
        let actual = Registry.Actor.findUnderPath (Path.create "/")
        let expected = actors
        actual |> should equal expected