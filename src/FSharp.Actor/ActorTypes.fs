namespace FSharp.Actor

open System
open System.Threading
open FSharp.Actor.Diagnostics

#if INTERACTIVE
open FSharp.Actor
open FSharp.Actor.Diagnostics
#endif

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
    Id : uint64 option
    Sender : ActorRef
    Message : 'a
}
with
    static member Create(msg, sender, ?id) =
        { Id = id; Message = msg; Sender = sender }
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
    | RemoveParent of ActorRef
    | Errored of ErrorContext

type ActorCell<'a> = {
    Children : ActorRef list
    Mailbox : IMailbox<Message<'a>>
    Self : IActor
    mutable ParentId : uint64 option
    mutable SpanId : uint64
    mutable Sender : ActorRef
}
with 
    member internal x.TryReceive(?timeout) = 
        async { return! x.Mailbox.TryReceive(defaultArg timeout Timeout.Infinite) }
    member internal x.Receive(?timeout) = 
        async { return! x.Mailbox.Receive(defaultArg timeout Timeout.Infinite) }
    member internal x.TryScan(f, ?timeout) = 
        async { return! x.Mailbox.TryScan(defaultArg timeout Timeout.Infinite, f) }
    member internal x.Scan(f, ?timeout) = 
        async { return! x.Mailbox.Scan(defaultArg timeout Timeout.Infinite, f) }
    member internal x.Path = x.Self.Path