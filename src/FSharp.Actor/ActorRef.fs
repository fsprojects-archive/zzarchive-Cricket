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
        member x.Path = 
          match x with
          | ActorRef(a) -> a.Path
          | Null -> ActorPath.empty

and IActor = 
    inherit IDisposable
    abstract Path : ActorPath with get
    abstract Post : obj * ActorRef -> unit

type IActor<'a> = 
    inherit IDisposable
    abstract Path : ActorPath with get
    abstract Post : 'a * ActorRef -> unit

type Message<'a> = {
    Sender : ActorRef
    Message : 'a
}

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

[<AutoOpen>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ActorRef = 
    
    open System.Runtime.Remoting.Messaging

    let internal getActorContext() = 
        match CallContext.LogicalGetData("actor") with
        | null -> None
        | :? ActorRef as a -> Some a
        | _ -> failwith "Unexpected type representing actorContext" 

    let sender() = 
        match getActorContext() with
        | None -> Null
        | Some ref -> ref
 
    let post (target:ActorRef) (msg:'a) = 
        match target with
        | ActorRef(actor) -> 
            let sender = sender()
            actor.Post(msg,sender)
        | _ -> ()

    let postWithSender (target:ActorRef) (sender:ActorRef) (msg:'a) = 
        match target with
        | ActorRef(actor) -> 
            actor.Post(msg,sender)
        | _ -> ()

type ActorRef with
    static member (-->) (msg,target) = post target msg
    static member (<--) (target,msg)  = post target msg