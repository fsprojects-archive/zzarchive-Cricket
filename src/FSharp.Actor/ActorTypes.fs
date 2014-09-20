namespace FSharp.Actor

open System
open System.Threading

type ActorRef = 
    | ActorRef of IActor
    | Null
    with
        override x.ToString() = 
            match x with
            | ActorRef(actor) -> actor.Path.ToString()
            | Null -> "ActorRef(Null)"
        member x.Path 
            with get() =
                match x with
                | ActorRef(actor) -> actor.Path
                | _ -> ActorPath.deadLetter
        member x.Post(msg : 'a, sender, ?id) = 
             x.Post(Message<'a>.Create(msg, sender, ?id = id))
        member x.Post(msg) = 
            match x with
            | ActorRef(actor) -> actor.Post(msg)
            | Null -> 
                //This really, really, should never ever happen
                raise(InvalidOperationException("Cannot send a message to a null actor reference"))

and Message<'a> = {
    Id : uint64
    Sender : ActorRef
    Message : 'a
}
with
    static member Create(msg, sender, ?id) =
        { Id = defaultArg id (Random.randomLong()); Message = msg; Sender = sender }
    static member Unbox<'a>(objM:Message<obj>) = { Id = objM.Id; Sender = objM.Sender; Message = unbox<'a> objM.Message }
    static member Box(objM:Message<'a>) = { Id = objM.Id; Sender = objM.Sender; Message = box objM.Message }

and IActor = 
    inherit IDisposable
    abstract Path : ActorPath with get
    abstract Post : Message<obj> -> unit

type IActor<'a> = 
    inherit IDisposable
    abstract Path : ActorPath with get
    abstract Post : Message<'a> -> unit

type ActorEvent = 
    | ActorStarted of ActorRef
    | ActorShutdown of ActorRef
    | ActorRestart of ActorRef
    | ActorErrored of ActorRef * exn
    | ActorAddedChild of ActorRef * ActorRef
    | ActorRemovedChild of ActorRef * ActorRef

type ActorStatus = 
    | Running 
    | Errored of exn
    | Stopped

type ErrorContext = {
    Error : exn
    Sender : ActorRef
    Children : ActorRef list
} 

type SystemMessage =
    | Shutdown
    | RestartTree
    | Restart
    | Link of ActorRef
    | Unlink of ActorRef
    | SetParent of ActorRef
    | Errored of ErrorContext