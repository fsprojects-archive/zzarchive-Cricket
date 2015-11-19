namespace Cricket.Tests

open System.Threading
open NUnit.Framework
open Cricket

[<TestFixture; Category("Unit")>]
type ``Given an event stream``() = 

     [<Test>]
     member __.``I can post and subscribe to events``() =
        let es = new DefaultEventStream("test") :> IEventStream
        let resultGate = new ManualResetEvent(false)
        let result = ref 0
        es.Subscribe(fun (x:int) -> result := x; resultGate.Set() |> ignore)

        es.Publish(10)

        if resultGate.WaitOne(1000)
        then Assert.AreEqual(10, !result)
        else Assert.Fail("No result timeout") 

     [<Test>]
     member __.``I can remove a subscription post and subscribe to events by type``() =
        let es = new DefaultEventStream("test") :> IEventStream
        let resultGate = new ManualResetEvent(false)
        let result = ref 0
        es.Subscribe(fun (x:int) -> result := x; resultGate.Set() |> ignore)

        es.Publish(10)

        if resultGate.WaitOne(2000)
        then 
            Assert.AreEqual(10, !result)
            es.Unsubscribe<int>()
            Thread.Sleep(1000)
            es.Publish(12)
            resultGate.WaitOne(2000) |> ignore
            Assert.AreNotEqual(12, !result)
        else Assert.Fail("No result timeout") 

     [<Test>]
     member __.``I can remove a subscription post and subscribe to events by key``() =
        let es = new DefaultEventStream("test") :> IEventStream
        let resultGate = new ManualResetEvent(false)
        let result = ref 0
        es.Subscribe("counters", fun (x:Event) -> 
            result := unbox<_> x.Payload; 
            resultGate.Set() |> ignore
        )

        es.Publish("counters", 10)
        if resultGate.WaitOne(2000)
        then 
            Assert.AreEqual(10, !result)
            es.Unsubscribe("counters")
            Thread.Sleep(1000)
            es.Publish("counters", 12)
            resultGate.WaitOne(2000) |> ignore
            Assert.AreNotEqual(12, !result)
        else Assert.Fail("No result timeout") 
        

     [<Test>]
     member __.``I can post and subscribe to events with an event key``() =
        let es = new DefaultEventStream("test") :> IEventStream
        let resultGate = new ManualResetEvent(false)
        let result = ref 0
        es.Subscribe("counters", fun (x:Event) -> result := unbox<_> x.Payload; resultGate.Set() |> ignore)

        es.Publish("counters", 10)

        if resultGate.WaitOne(2000)
        then Assert.AreEqual(10, !result)
        else Assert.Fail("No result timeout") 
