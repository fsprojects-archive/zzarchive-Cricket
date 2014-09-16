namespace FSharp.Actor

open System

type actorRef = 
    | ActorRef of IActor
    | Null
    with
        override x.ToString() = 
            match x with
            | ActorRef(actor) -> actor.Path.ToString()
            | Null -> "ActorRef(Null)"

and IActor = 
    inherit IDisposable
    abstract Path : actorPath with get
    abstract Post : obj * actorRef -> unit

type IActor<'a> = 
    inherit IDisposable
    abstract Path : actorPath with get
    abstract Post : 'a * actorRef -> unit

[<AutoOpen>]
module ActorRef = 
    
    open System.Runtime.Remoting.Messaging

    let path = function
        | ActorRef(a) -> a.Path
        | Null -> ActorPath.empty

    let internal getActorContext() = 
        match CallContext.LogicalGetData("actor") with
        | null -> None
        | :? actorRef as a -> Some a
        | _ -> failwith "Unexpected type representing actorContext" 

    let sender() = 
        match getActorContext() with
        | None -> Null
        | Some ref -> ref
 
    let post (target:actorRef) (msg:'a) = 
        match target with
        | ActorRef(actor) -> 
            let sender = sender()
            actor.Post(msg,sender)
        | _ -> ()

    let postWithSender (target:actorRef) (sender:actorRef) (msg:'a) = 
        match target with
        | ActorRef(actor) -> 
            actor.Post(msg,sender)
        | _ -> ()

type actorRef with
    static member (-->) (msg,target) = post target msg
    static member (<--) (target,msg)  = post target msg
    
[<AutoOpen>]
module ActorRefOperations = 
    
    let inline (<-*) (targets, msg) = Seq.iter (fun (t:actorRef) -> t <-- msg) targets 