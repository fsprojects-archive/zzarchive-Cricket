(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor
open System.Threading

(**

Metrics
=======

*)


ActorHost.Configure(fun c -> c.Metrics <- Some { ReportInterval = 5000; Handler = HttpEndpoint 9797 })
ActorHost.Start()
let system = ActorHost.CreateSystem("greeterSystem")

type Say =
    | Hello
    | HelloWorld
    | Name of string

let greeter = 
    actor {
        name "greeter"
        messageHandler (fun actor ->
            let rec loop() = async {
                let! msg = actor.Receive() //Wait for a message

                match msg.Message with
                | Hello ->  actor.Logger.Debug("Hello") //Handle Hello leg
                | HelloWorld -> actor.Logger.Debug("Hello World") //Handle HelloWorld leg
                | Name name -> actor.Logger.Debug(sprintf "Hello, %s" name) //Handle Name leg
                return! loop() //Recursively loop

            }
            loop())
    } |> system.SpawnActor

let cts = new CancellationTokenSource()
let rec publisher() = async {
    do! Async.Sleep(1)
    do greeter <-- Name "Metrics"
    return! publisher()
}

Async.Start(publisher(), cts.Token)
cts.Cancel()

greeter <-- Shutdown