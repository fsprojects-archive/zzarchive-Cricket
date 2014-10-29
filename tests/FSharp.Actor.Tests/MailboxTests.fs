namespace Cricket.Tests

open System.Threading
open NUnit.Framework
open FsUnit
open Cricket

[<TestFixture; Category("Unit")>]
type ``Given a mailbox``() = 

    [<Test>]
    member __.``I can receive a message``() =
        let mailbox = (new DefaultMailbox<int>("test") :> IMailbox<int>)

        mailbox.Post(10)
        let result = Async.RunSynchronously(mailbox.Receive())
        result |> should equal 10
    
    [<Test>]
    member __.``I receive None when no message after timeout period``() = 
        let mailbox = (new DefaultMailbox<int>("test") :> IMailbox<int>)
        let resultGate = new ManualResetEventSlim(false)

        let result = ref (Some(0))

        let receiver = 
            async {
                let! msg = mailbox.TryReceive(100) 
                result := msg
                resultGate.Set()
            }
        
        Async.Start(receiver)

        if resultGate.Wait(1000)
        then !result |> should equal None
        else Assert.Fail("No result timeout") 

    [<Test>]
    member __.``I can receive None when timing out scanning for a messsage``() = 
        let mailbox = (new DefaultMailbox<int>("test") :> IMailbox<int>)
        let resultGate = new ManualResetEventSlim(false)

        let result = ref (Some 10)

        let receiver = 
            async {
                let! msg = mailbox.TryScan(100, (fun x -> if x = 10 then Some(async { return x }) else None)) 
                result := msg
                resultGate.Set()
            }
        
        Async.Start(receiver)

        let producer = 
            async {
                do! Async.Sleep(400)
                do mailbox.Post(2)
                do! Async.Sleep(400)
                do mailbox.Post(6)
                do! Async.Sleep(400)
                do mailbox.Post(10)
            }

        Async.Start(producer)

        if resultGate.Wait(1000)
        then !result |> should equal None
        else Assert.Fail("No result timeout") 

    [<Test>]
    member __.``I can scan for a messsage``() = 
        let mailbox = (new DefaultMailbox<int>("test") :> IMailbox<int>)
        let resultGate = new ManualResetEventSlim(false)
        
        let result = ref 0

        let receiver = 
            async {
                let! msg = mailbox.Scan(fun x -> if x = 10 then Some(async { return x }) else None) 
                result := msg
                resultGate.Set()
            }
        
        Async.Start(receiver)

        let producer = 
            async {
                do mailbox.Post(2)
                do! Async.Sleep(400)
                do mailbox.Post(6)
                do! Async.Sleep(400)
                do mailbox.Post(10)
            }

        Async.Start(producer)

        if resultGate.Wait(1000)
        then !result |> should equal 10
        else Assert.Fail("No result timeout") 
        
