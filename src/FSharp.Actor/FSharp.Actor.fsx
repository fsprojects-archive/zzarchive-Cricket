#I @"..\..\bin"
#r "FSharp.Actor.dll"

#load "Math.fs"
#load "Metrics.fs"

open System
open System.Threading
open FSharp.Actor


//
let cts = new CancellationTokenSource()
let rnd = Random()
let ctx = Metrics.createContext "testContext"

let guageMetric = Metrics.createGuage ctx "myGuage"
let counterMetric = Metrics.createCounter ctx "myCounter"
let timerMetric = Metrics.createTimer ctx "myTimer"

let rec reporter() = 
    async {
       do! Async.Sleep(1000)
       printfn "%A" (Metrics.createReport())
       return! reporter()        
    }

let rec guageCreator() = 
    async {
        do guageMetric (int64(rnd.Next()))
        do! Async.Sleep(400)
        return! guageCreator() 
    }

let rec counterCreator() = 
    async {
        do counterMetric (int64(rnd.Next(1,10)))
        do! Async.Sleep(400)
        return! counterCreator() 
    }

let rec timerCreator() = async { 
    do timerMetric (fun () -> Thread.Sleep(5))
    return! timerCreator()
}

Async.Start(guageCreator(), cts.Token)
Async.Start(timerCreator(), cts.Token)
Async.Start(counterCreator(), cts.Token)

Async.Start(reporter() , cts.Token)

cts.Cancel()
