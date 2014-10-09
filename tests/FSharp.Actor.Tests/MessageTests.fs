namespace FSharp.Actor.Tests

open System
open System.Threading
open NUnit.Framework
open FsUnit
open FSharp.Actor

type RecordingActor(path) =
    let messages = new ResizeArray<Message<int>>()
    
    interface IActor with
        member x.Path with get() = ActorPath.ofString path
        member x.Post(msg) = messages.Add(Message.map unbox msg)
        member x.Dispose() = ()

[<TestFixture; Category("Unit")>]
type ``Given an Message Handler``() =
    
    let getCell() =
        let simpleMailbox = new DefaultMailbox<Message<int>>("test")
        ActorCell<int>.Create(new RecordingActor("actor/test"), simpleMailbox)

    [<Test>]
    member __.``I can recieve a message``() =
        let cell = getCell()
        let resultGate = new ManualResetEventSlim(false)
        let result = ref 0

        messageHandler {
            let! msg = Message.receive()
            result := msg
            resultGate.Set()
        } |> MessageHandler.toAsync cell |> Async.Start

        cell.Mailbox.Post(Message.create<int> None 10)

        if resultGate.Wait(5000)
        then !result |> should equal 10
        else Assert.Fail()



