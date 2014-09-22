#I "../../bin"
#r "FSharp.Actor.dll"
open FSharp.Actor

ActorHost.Start()

type Say =
    | Hello

let greeter = 
    actor {
        name "greeter"
        body (
            let rec loop() = messageHandler {
                printfn "Wating for message"
                let! msg = Message.receive None
                printfn "Received Message"
                match msg with
                | Hello ->  printfn "Hello"
                return! loop()

            }
            loop())
    } |> Actor.spawn

greeter <-- Hello


