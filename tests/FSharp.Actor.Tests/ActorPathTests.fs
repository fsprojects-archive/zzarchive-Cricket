namespace FSharp.Actor.Tests

open System
open NUnit.Framework
open FsUnit
open FSharp.Actor

[<TestFixture; Category("Unit")>]
type ``Given a Actor Path``() = 

    [<Test>]
    member t.``I can get the all of the lookup paths corresponding to the path``() =
        let path = ActorPath.T("a/b")
        printfn "Paths: %A" path.LookupPaths
        path.LookupPaths |> should equal [["/"];["a"];["a";"b"]]

    [<Test>]
    member t.``/ should represent the root path``() = 
        let path = ActorPath.T("/")
        path.Keys |> should equal ["/"]

    [<Test>]
    member t.``I can create an ActorPath from a full Uri``() =
        let uri = "actor://" + Environment.MachineName + "/a/b"
        let path = ActorPath.T(uri)
        path.AbsoluteUri |> should equal (uri.ToLower())

    [<Test>]
    member t.``it should be lower case``() =
        let path = ActorPath.T("Me")
        path.AbsoluteUri |> should equal ("actor://"+ (Environment.MachineName.ToLower()) + "/me")