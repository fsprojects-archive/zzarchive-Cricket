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
        member x.Post(msg, sender) = 
            match x with
            | ActorRef(actor) -> actor.Post(msg, sender)
            | Null -> 
                //This really, really, should never ever happen
                raise(InvalidOperationException("Cannot send a message to a null actor reference"))

and Message<'a> = {
    Sender : ActorRef
    Message : 'a
}

and IActor = 
    inherit IDisposable
    abstract Path : ActorPath with get
    abstract Post : obj * ActorRef -> unit

type IActor<'a> = 
    inherit IDisposable
    abstract Path : ActorPath with get
    abstract Post : 'a * ActorRef -> unit

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
           

type RemoteMessage = {
    Target : ActorPath
    Sender : ActorPath
    Message : obj
}

type ITransport =
    inherit IDisposable
    abstract Scheme : string with get
    abstract BasePath : ActorPath with get
    abstract Post : ActorPath * RemoteMessage -> unit
    abstract Start : ISerializer * CancellationToken -> unit