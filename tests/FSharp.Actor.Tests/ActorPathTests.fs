namespace FSharp.Actor.Tests

open System
open NUnit.Framework
open FsUnit
open FSharp.Actor

[<TestFixture; Category("Unit")>]
type ``Given a Actor Path``() = 

    [<Test>]
    member t.``I can get the all of the keys corresponding to the path``() =
        let keys = Path.keys (Path.create "a/b")
        printfn "%A" keys
        keys |> should equal ["actor";(Environment.MachineName.ToLower());"a";"b"]

    [<Test>]
    member t.``/ should represent the root path for the local machine``() = 
        let path = Path.create ("/")
        let keys = Path.keys path
        printfn "%A" keys
        Path.keys path |> should equal ["actor"; System.Environment.MachineName.ToLowerInvariant()]

    [<Test>]
    member t.``I can create an ActorPath from a full Uri``() =
        let uri = "actor://" + Environment.MachineName + "/a/b"
        let path = Path.create uri
        path.AbsoluteUri |> should equal (uri.ToLower())

    [<Test>]
    member t.``it should be lower case``() =
        let path = Path.create "Me"
        path.AbsoluteUri |> should equal ("actor://"+ (Environment.MachineName.ToLower()) + "/me")