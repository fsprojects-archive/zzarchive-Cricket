(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FsPickler.dll"
#r "Cricket.dll"
open Cricket
open Cricket.Diagnostics
open System.IO
open System.Threading
open Nessos.FsPickler

(**

Metrics
=======

*)

ActorHost.Start()

type Say =
    | Hello
    | HelloWorld
    | Name of string

let greeter = 
    actor {
        name "greeter"
        body (
            let rec loop() = messageHandler {
                let! msg = Message.receive() //Wait for a message

                match msg with
                | Hello ->  printfn "Hello" //Handle Hello leg
                | HelloWorld -> printfn "Hello World" //Handle HelloWorld leg
                | Name name -> printfn "Hello, %s" name //Handle Name leg
                return! loop() //Recursively loop

            }
            loop())
    } |> Actor.spawn

let cts = new CancellationTokenSource()

let rec publisher() = async {
    do greeter <-- Name "Metrics"
    return! publisher()
}

(**
Write the metrics out to a file on a background thread

*)

let rec reporter() = 
    let pickler = FsPickler.CreateXml()
    async {
       do! Async.Sleep(5000)
       do File.WriteAllBytes(@"C:\temp\Example.metrics", Metrics.getReport() |> pickler.Pickle) 
       return! reporter()        
    }

(**
Start everything
*)
Async.Start(reporter(), cts.Token)  
Async.Start(publisher(), cts.Token)

cts.Cancel()

greeter <-- Shutdown

(**
Sample metrics output
explain exponential weights, and different types of counters.
*)