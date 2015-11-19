namespace Cricket.Tests

open System
open NUnit.Framework
open Cricket

[<TestFixture; Category("Unit")>]
type ``With Actor Path``() = 

    [<Test>]
    member t.``I can build an actor path from an absolute uri``() = 
        let result = ActorPath.ofString "actor.tcp://localMachineAddress:6667/actor/mine"
        Assert.AreEqual("actor.tcp://*@localMachineAddress:6667/actor/mine", result.ToString())

    [<Test>]
    member t.``I can build an actor path from a realtive uri``() = 
        let result = ActorPath.ofString "/actor/mine"
        Assert.AreEqual("actor://*@*/actor/mine", result.ToString())

    [<Test>]
    member t.``I can build an actor path from a realtive uri with a Host``() = 
        let result = ActorPath.ofString "/node1@/actor/mine"
        Assert.AreEqual("actor://node1@*/actor/mine", result.ToString())

    [<Test>]
    member t.``I can build an actor path with wildcards on everything but path component``() = 
        let result = ActorPath.ofString "*://*@*/actor/mine"
        Assert.AreEqual(None, result.Transport)
        Assert.AreEqual(None, result.MachineAddress)
        Assert.AreEqual(None, result.MachineAddressType)
        Assert.AreEqual(["actor"; "mine"],result.PathComponents)
        Assert.AreEqual(None, result.Host)
        Assert.AreEqual(None, result.Port)

    [<Test>]
    member t.``I can build an actor path with wildcards on everything but path component and transport``() = 
        let result = ActorPath.ofString "actor.transport://*@*/actor/mine"
        Assert.AreEqual(None,result.MachineAddress)
        Assert.AreEqual(None,result.MachineAddressType)
        Assert.AreEqual(["actor"; "mine"],result.PathComponents)
        Assert.AreEqual(None,result.Host)
        Assert.AreEqual(None,result.Port)
        Assert.AreEqual((Some "actor.transport"),result.Transport)

    [<Test>]
    member t.``I can get the local path``() = 
        let result = ActorPath.ofString "actor.transport://*@*/actor/mine"
        Assert.AreEqual(None,result.MachineAddress)
        Assert.AreEqual(None,result.MachineAddressType)
        Assert.AreEqual(["actor"; "mine"],result.PathComponents)
        Assert.AreEqual(None,result.Host)
        Assert.AreEqual(None,result.Port)
        Assert.AreEqual((Some "actor.transport"),result.Transport)

    [<Test>]
    member t.``I can build an actor path with wildcards on everything but path component and Host``() = 
        let result = ActorPath.ofString "*://Host1@*/actor/mine"
        Assert.AreEqual(None,result.MachineAddress)
        Assert.AreEqual(None,result.MachineAddressType)
        Assert.AreEqual(["actor"; "mine"],result.PathComponents)
        Assert.AreEqual(Some("Host1"),result.Host)
        Assert.AreEqual(None,result.Port)
        Assert.AreEqual(None,result.Transport)

    [<Test>]
    member t.``I can build an actor path and detect correct MachineAddressname and type DNS``() = 
        let result = ActorPath.ofString "*://*@localMachineAddress:6667/actor/mine"
        Assert.AreEqual((Some "localMachineAddress"),result.MachineAddress)
        Assert.AreEqual((Some UriHostNameType.Dns),result.MachineAddressType)
        Assert.AreEqual(["actor"; "mine"],result.PathComponents)
        Assert.AreEqual(None,result.Host)
        Assert.AreEqual((Some 6667),result.Port)
        Assert.AreEqual(None,result.Transport)
        
    [<Test>]
    member t.``I can build an actor path and detect correct MachineAddressname and type IPV4``() = 
        let result = ActorPath.ofString "*://*@192.168.1.2:6667/actor/mine"
        Assert.AreEqual((Some "192.168.1.2"),result.MachineAddress)
        Assert.AreEqual((Some UriHostNameType.IPv4),result.MachineAddressType)
        Assert.AreEqual(["actor"; "mine"],result.PathComponents)
        Assert.AreEqual(None,result.Host)
        Assert.AreEqual((Some 6667),result.Port)
        Assert.AreEqual(None,result.Transport)
        

    [<Test>]
    member t.``I can rebase with a defined Host in original path``() =
        let basePath = ActorPath.ofString "actor.tcp://localMachineAddress:6667/"
        let actorPath = ActorPath.ofString "actor://node1@localMachineAddress/actor/mine"
        let expected = ActorPath.ofString "actor.tcp://node1@localMachineAddress:6667/actor/mine"
        let result = ActorPath.rebase basePath actorPath
        Assert.AreEqual(expected, result)

    [<Test>]
    member t.``Should stay unchanged when a realtive URI is used to rebase``() = 
        let basePath = ActorPath.ofUri(new Uri("/base/path", UriKind.Relative))
        let actorPath = ActorPath.ofString "actor://*@localMachineAddress/actor/mine"
        let result = ActorPath.rebase basePath actorPath
        Assert.AreEqual(actorPath, result)
        
    [<Test>]
    member t.``I should be able to get the components of an actor path``() = 
        let path = ActorPath.ofString "actor.tcp://node1@localMachineAddress:6667/actor/mine"
        let components = ActorPath.components path
        let expected = [TrieKey.Key("node1"); TrieKey.Key("actor"); TrieKey.Key("mine")]
        Assert.AreEqual(expected, components)