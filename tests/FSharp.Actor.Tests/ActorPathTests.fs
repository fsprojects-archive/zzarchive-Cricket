namespace FSharp.Actor.Tests

open System
open NUnit.Framework
open FsUnit
open FSharp.Actor

[<TestFixture; Category("Unit")>]
type ``With Actor Path``() = 

    [<Test>]
    member t.``I can build an actor path from an absolute uri``() = 
        let result = ActorPath.ofString "actor.tcp://localhost:6667/actor/mine"
        result.ToString() |> should equal "actor.tcp://*@localhost:6667/actor/mine"

    [<Test>]
    member t.``I can build an actor path from a realtive uri``() = 
        let result = ActorPath.ofString "/actor/mine"
        result.ToString() |> should equal "*://*@*/actor/mine"

    [<Test>]
    member t.``I can build an actor path from a realtive uri with a system``() = 
        let result = ActorPath.ofString "/node1@/actor/mine"
        result.ToString() |> should equal "*://node1@*/actor/mine"

    [<Test>]
    member t.``I can build an actor path with wildcards on everything but path component``() = 
        let result = ActorPath.ofString "*://*@*/actor/mine"
        result.Transport |> should equal None
        result.Host |> should equal None
        result.HostType |> should equal None
        result.Path |> should equal ["actor"; "mine"]
        result.System |> should equal None
        result.Port |> should equal None

    [<Test>]
    member t.``I can build an actor path with wildcards on everything but path component and transport``() = 
        let result = ActorPath.ofString "actor.transport://*@*/actor/mine"
        result.Host |> should equal None
        result.HostType |> should equal None
        result.Path |> should equal ["actor"; "mine"]
        result.System |> should equal None
        result.Port |> should equal None
        result.Transport |> should equal (Some "actor.transport")

    [<Test>]
    member t.``I can build an actor path with wildcards on everything but path component and system``() = 
        let result = ActorPath.ofString "*://system1@*/actor/mine"
        result.Host |> should equal None
        result.HostType |> should equal None
        result.Path |> should equal ["actor"; "mine"]
        result.System |> should equal (Some "system1")
        result.Port |> should equal None
        result.Transport |> should equal None

    [<Test>]
    member t.``I can build an actor path and detect correct hostname and type DNS``() = 
        let result = ActorPath.ofString "*://*@localhost:6667/actor/mine"
        result.Host |> should equal (Some "localhost")
        result.HostType |> should equal (Some UriHostNameType.Dns)
        result.Path |> should equal ["actor"; "mine"]
        result.System |> should equal None
        result.Port |> should equal (Some 6667)
        result.Transport |> should equal None

    [<Test>]
    member t.``I can build an actor path and detect correct hostname and type IPV4``() = 
        let result = ActorPath.ofString "*://*@192.168.1.2:6667/actor/mine"
        result.Host |> should equal (Some "192.168.1.2")
        result.HostType |> should equal (Some UriHostNameType.IPv4)
        result.Path |> should equal ["actor"; "mine"]
        result.System |> should equal None
        result.Port |> should equal (Some 6667)
        result.Transport |> should equal None

    [<Test>]
    member t.``I can rebase with a defined system in original path``() =
        let basePath = ActorPath.ofString "actor.tcp://localhost:6667/"
        let actorPath = ActorPath.ofString "actor://node1@localhost/actor/mine"
        let expected = ActorPath.ofString "actor.tcp://node1@localhost:6667/actor/mine"
        let result = ActorPath.rebase basePath actorPath
        result |> should equal expected

    [<Test>]
    member t.``Should stay unchanged when a realtive URI is used to rebase``() = 
        let basePath = ActorPath.ofUri(new Uri("/base/path", UriKind.Relative))
        let actorPath = ActorPath.ofString "actor://*@localhost/actor/mine"
        let result = ActorPath.rebase basePath actorPath
        result |> should equal actorPath
        
    [<Test>]
    member t.``I should be able to get the components of an actor path``() = 
        let path = ActorPath.ofString "actor.tcp://node1@localhost:6667/actor/mine"
        let components = ActorPath.components path
        let expected = [Trie.Key("node1"); Trie.Key("actor"); Trie.Key("mine")]
        components |> should equal expected
