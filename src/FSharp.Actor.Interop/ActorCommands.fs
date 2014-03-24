namespace FSharp.Actor.Interop

open FSharp.Actor

[<AutoOpen>]
[<System.Runtime.CompilerServices.Extension>]
module ActorCommands =
    
    [<System.Runtime.CompilerServices.Extension>] 
    let FindChildren (actorPath: string) = 
        !* actorPath

    [<System.Runtime.CompilerServices.Extension>] 
    let Find (actorPath: string) =
        !! actorPath

    [<System.Runtime.CompilerServices.Extension>] 
    let TellAll (actors: seq<IActor>) (message: 'a) =
        actors <-* message

    [<System.Runtime.CompilerServices.Extension>] 
    let Tell (actor: IActor) (message: 'a) =
        actor <-- message

    [<System.Runtime.CompilerServices.Extension>] 
    let FindAndTell (actorPath: string) (message: 'a) =
        actorPath ?<-- message

    [<System.Runtime.CompilerServices.Extension>] 
    let TellSystemMessage (actor: IActor) (systemMessage: SystemMessage) =
        actor <!- systemMessage

    [<System.Runtime.CompilerServices.Extension>] 
    let FindAndTellSystemMessage (actorPath: string) (systemMessage: SystemMessage) =
        actorPath ?<!- systemMessage