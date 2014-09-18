namespace FSharp.Actor.Tests

open System
open NUnit.Framework
open FsUnit
open FSharp.Actor

[<TestFixture; Category("Unit")>]
type ``With Actor Path``() = 

    [<Test>]
    member t.``I can build an actor path from an absolute uri``() = 
        let result = ActorPath.ofString "actor.tcp://localMachineAddress:6667/actor/mine"
        result.ToString() |> should equal "actor.tcp://*@localMachineAddress:6667/actor/mine"

    [<Test>]
    member t.``I can build an actor path from a realtive uri``() = 
        let result = ActorPath.ofString "/actor/mine"
        result.ToString() |> should equal "*://*@*/actor/mine"

    [<Test>]
    member t.``I can build an actor path from a realtive uri with a Host``() = 
        let result = ActorPath.ofString "/node1@/actor/mine"
        result.ToString() |> should equal "*://node1@*/actor/mine"

    [<Test>]
    member t.``I can build an actor path with wildcards on everything but path component``() = 
        let result = ActorPath.ofString "*://*@*/actor/mine"
        result.Transport |> should equal None
        result.MachineAddress |> should equal None
        result.MachineAddressType |> should equal None
        result.PathComponents |> should equal ["actor"; "mine"]
        result.Host |> should equal None
        result.Port |> should equal None

    [<Test>]
    member t.``I can build an actor path with wildcards on everything but path component and transport``() = 
        let result = ActorPath.ofString "actor.transport://*@*/actor/mine"
        result.MachineAddress |> should equal None
        result.MachineAddressType |> should equal None
        result.PathComponents |> should equal ["actor"; "mine"]
        result.Host |> should equal None
        result.Port |> should equal None
        result.Transport |> should equal (Some "actor.transport")

    [<Test>]
    member t.``I can get the local path``() = 
        let result = ActorPath.ofString "actor.transport://*@*/actor/mine"
        result.MachineAddress |> should equal None
        result.MachineAddressType |> should equal None
        result.PathComponents |> should equal ["actor"; "mine"]
        result.Host |> should equal None
        result.Port |> should equal None
        result.Transport |> should equal (Some "actor.transport")

    [<Test>]
    member t.``I can build an actor path with wildcards on everything but path component and Host``() = 
        let result = ActorPath.ofString "*://Host1@*/actor/mine"
        result.MachineAddress |> should equal None
        result.MachineAddressType |> should equal None
        result.PathComponents |> should equal ["actor"; "mine"]
        result.Host |> should equal (Some "Host1")
        result.Port |> should equal None
        result.Transport |> should equal None

    [<Test>]
    member t.``I can build an actor path and detect correct MachineAddressname and type DNS``() = 
        let result = ActorPath.ofString "*://*@localMachineAddress:6667/actor/mine"
        result.MachineAddress |> should equal (Some "localMachineAddress")
        result.MachineAddressType |> should equal (Some UriHostNameType.Dns)
        result.PathComponents |> should equal ["actor"; "mine"]
        result.Host |> should equal None
        result.Port |> should equal (Some 6667)
        result.Transport |> should equal None

    [<Test>]
    member t.``I can build an actor path and detect correct MachineAddressname and type IPV4``() = 
        let result = ActorPath.ofString "*://*@192.168.1.2:6667/actor/mine"
        result.MachineAddress |> should equal (Some "192.168.1.2")
        result.MachineAddressType |> should equal (Some UriHostNameType.IPv4)
        result.PathComponents |> should equal ["actor"; "mine"]
        result.Host |> should equal None
        result.Port |> should equal (Some 6667)
        result.Transport |> should equal None

    [<Test>]
    member t.``I can rebase with a defined Host in original path``() =
        let basePath = ActorPath.ofString "actor.tcp://localMachineAddress:6667/"
        let actorPath = ActorPath.ofString "actor://node1@localMachineAddress/actor/mine"
        let expected = ActorPath.ofString "actor.tcp://node1@localMachineAddress:6667/actor/mine"
        let result = ActorPath.rebase basePath actorPath
        result |> should equal expected

    [<Test>]
    member t.``Should stay unchanged when a realtive URI is used to rebase``() = 
        let basePath = ActorPath.ofUri(new Uri("/base/path", UriKind.Relative))
        let actorPath = ActorPath.ofString "actor://*@localMachineAddress/actor/mine"
        let result = ActorPath.rebase basePath actorPath
        result |> should equal actorPath
        
    [<Test>]
    member t.``I should be able to get the components of an actor path``() = 
        let path = ActorPath.ofString "actor.tcp://node1@localMachineAddress:6667/actor/mine"
        let components = ActorPath.components path
        let expected = [Trie.Key("node1"); Trie.Key("actor"); Trie.Key("mine")]
        components |> should equal expected