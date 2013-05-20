namespace FSharp.Actor.Tests

open NUnit.Framework
open FsUnit
open FSharp.Actor

[<TestFixture; Category("Unit")>]
type ``Given a Actor Trie``() = 
    
    [<SetUp>]
    member t.setup() =
        ActorTrie.clear()

    [<Test>]
    member t.``I can register an actor``() =
        let actor = (new NullActor("a")).Ref()
        ActorTrie.add actor.Path (actor)

        let actual = ActorTrie.instance.Value 
        let expected =
            Trie.Node(None,
                       Map [("/", Trie.Node (None, 
                                        Map [("a", 
                                                Trie.Node (Some actor,Map [])
                                            )])
                           )])
        actual |> should equal expected

    [<Test>]
    member t.``I can unregister an actor``() =
        let actor = (new NullActor("a")).Ref()
        ActorTrie.add actor.Path (actor)
        ActorTrie.remove actor.Path

        ActorTrie.instance.Value |> should equal ActorTrie.empty

    [<Test>]
    member t.``I can find an actor by path``() =
        let actors = List.init 5 (fun i -> new NullActor(sprintf "a/%d" i))
        actors |> List.iter ActorTrie.addActor
        let actual = ActorTrie.tryFind (ActorPath.T("a/4"))
        let expected = [actors.[4]]
        actual |> should equal expected

    [<Test>]
    member t.``/ returns all of the actors``() =
        let actors = List.init 5 (fun i -> (new NullActor(sprintf "a/%d" i)).Ref())
        actors |> List.iter ActorTrie.addActor
        let actual = ActorTrie.tryFind (ActorPath.T("/"))
        let expected = actors
        actual |> should equal expected