namespace FSharp.Actor

open System


type RemoteActorRef(path:ActorPath.T, transportRef : ActorRef) =
    inherit ActorRef(path) 
    override x.Post(msg) = transportRef.Post(Post(x.Path, msg))
    override x.QueueLength with get() = raise(NotImplementedException())
    override x.Watch(_) = raise(NotImplementedException())
    override x.Unwatch() = raise(NotImplementedException())
    override x.Link(_) = raise(NotImplementedException())
    override x.Unlink(_) = raise(NotImplementedException())
    override x.Status with get() = raise(NotImplementedException())
    override x.Children with get() = Seq.empty

