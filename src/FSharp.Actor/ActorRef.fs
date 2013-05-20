namespace FSharp.Actor

open System

type ActorStatus = 
    | Errored
    | OK
    | Restarting
    | Shutdown

type [<AbstractClass>]ActorRef(path : ActorPath.T) =
    let path = path
    member val Path = path  with get

    abstract Post : obj-> unit
    abstract Watch : ActorRef -> unit
    abstract Unwatch : unit -> unit
    abstract Link : ActorRef -> unit
    abstract Unlink : ActorRef -> unit
    abstract Status : ActorStatus with get
    abstract Children : seq<ActorRef> with get
    abstract QueueLength : int with get
  
    override x.ToString() = x.Path.AbsoluteUri

    override x.Equals(y:obj) = 
        match y with
        | :? ActorRef as y -> x.Path = y.Path
        | _ -> false
    override x.GetHashCode() = x.Path.GetHashCode()
    interface IComparable with
        member x.CompareTo(y:obj) = 
            match y with
            | :? ActorRef as y -> x.Path.AbsoluteUri.CompareTo(y.Path.AbsoluteUri)
            | _ -> raise(InvalidOperationException())


    


