namespace FSharp.Actor.Interop

open FSharp.Actor

[<AutoOpen>]
module ActorCommands =
    
    let FindChildren (actorPath: string) = 
        !* actorPath

    let Find (actorPath: string) =
        !! actorPath

    let TellAll (actors: seq<IActor>) (message: 'a) =
        actors <-* message

    let Tell (actor: IActor) (message: 'a) =
        actor <-- message

    let FindAndTell (actorPath: string) (message: 'a) =
        actorPath ?<-- message

    let TellSystemMessage (actor: IActor) (systemMessage: SystemMessage) =
        actor <!- systemMessage

    let FindAndTellSystemMessage (actorPath: string) (systemMessage: SystemMessage) =
        actorPath ?<!- systemMessage