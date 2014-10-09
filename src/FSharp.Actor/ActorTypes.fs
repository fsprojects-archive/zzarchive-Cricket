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
             x.Post({ Id = id; Message = box msg; Sender = sender })
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
    | ActorLinked of ActorRef * ActorRef
    | ActorUnLinked of ActorRef * ActorRef

type ActorStatus = 
    | Running 
    | Errored of exn
    | Stopped

type ErrorContext = {
    Error : exn
    Sender : ActorRef
} 

type SystemMessage =
    | Shutdown
    | Restart
    | Link
    | UnLink

type SupervisorMessage = 
    | Error of exn
    | ChildLink
    | ChildUnLink
    | ChildShutdown
    | ChildRestart

type ActorCell<'a> = {
    Mailbox : IMailbox<Message<'a>>
    Self : ActorRef
    mutable ParentId : uint64 option
    mutable SpanId : uint64
    mutable Sender : ActorRef
}
with
    static member Create(ref:ActorRef, mailbox, ?parentid, ?spanid, ?sender) =
        {
            Self = ref
            Mailbox = mailbox
            ParentId = parentid
            SpanId = (defaultArg spanid 0UL)
            Sender = (defaultArg sender ActorRef.Null)
        }
    static member Create(actor:IActor, mailbox, ?parentid, ?spanid, ?sender) = 
        ActorCell<'a>.Create(ActorRef actor, mailbox, ?parentid = parentid, ?spanid = spanid, ?sender = sender)