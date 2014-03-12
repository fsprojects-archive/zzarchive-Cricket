namespace FSharp.Actor.Remoting

open System.Threading
open FSharp.Actor
open FSharp.Actor.Actor
open FSharp.Actor.Types

module RemoteActor = 
    
    type T<'a>(remoteAddress:ActorPath, transport:ITransport, ?options)=
        let cts = new CancellationTokenSource()
        let mutable status = ActorStatus.Shutdown("")
        let mutable options = defaultArg options (Actor.Options<'a>.Create(address = remoteAddress))
        let mutable supervisorInbox = new DefaultMailbox<SupervisorMessage>() :> IMailbox<SupervisorMessage>
        let preStart = new Event<IActor>()
        let preRestart = new Event<IActor>()
        let preStop = new Event<IActor>()
        let onStopped = new Event<IActor>()
        let onRestarted = new Event<IActor>()
        let onStart = new Event<IActor>()
        let children = new ResizeArray<IActor>()

        let setStatus newStatus = 
            status <- newStatus

        override x.ToString() =  (x :> IActor).Id
        override x.Equals(y:obj) = 
            match y with
            | :? IActor as y -> (x :> IActor).Id = y.Id
            | _ -> false
        override x.GetHashCode() = (x :> IActor).Id.GetHashCode()

        interface IRemoteActor<'a> with
            
            [<CLIEvent>]
            member x.OnRestarted = onRestarted.Publish
            [<CLIEvent>]
            member x.OnStarted = onStart.Publish
            [<CLIEvent>]
            member x.OnStopped = onStopped.Publish
            [<CLIEvent>]
            member x.PreRestart = preRestart.Publish
            [<CLIEvent>]
            member x.PreStart = preStart.Publish
            [<CLIEvent>]
            member x.PreStop = preStop.Publish
    
            member x.Id with get()= options.Id
            member x.Path with get()= options.Path
            member x.QueueLength with get() = 0
    
            member x.Start() =
                preStart.Trigger(x :> IActor)
                setStatus ActorStatus.Running
                onStart.Trigger(x :> IActor)
            
            member x.Post(msg : obj, ?sender:IActor) = (x :> IRemoteActor<'a>).Post(msg :?> 'a, sender)
            member x.Post(msg : 'a, ?sender) =
                options.Logger.Debug(sprintf "%A sending %A to %A" x msg remoteAddress, None)
                //TODO: Maybe the transport should just return an endpoint we can write to
                transport.Send(remoteAddress, msg, sender) 
    
            member x.Post(msg : 'a) = (x :> IRemoteActor<'a>).Post(msg, Option<IActor>.None)
    
            member x.PostSystemMessage(sysMessage : SystemMessage, ?sender : IActor) =
                transport.SendSystemMessage(remoteAddress, sysMessage,  sender)

            member x.UnderlyingTransport = transport
    
            member x.Link(actorRef) = children.Add(actorRef)
            member x.UnLink(actorRef) = children.Remove(actorRef) |> ignore
            member x.Watch(supervisor) = 
                match supervisor with
                | :? IActor<SupervisorMessage> as sup -> 
                    options <- { options with Supervisor = Some sup }
                    supervisor.Link(x)
                | _ -> failwithf "The IActor passed to watch must be of type IActor<SupervisorMessage>"
            member x.UnWatch() =
               match options.Supervisor with
               | Some(sup) -> 
                   options <- { options with Supervisor = None }
                   sup.UnLink(x)
               | None -> () 
            member x.Children with get() = children :> seq<_>
            member x.Status with get() = status
            
    let create remoteAddress transport options = 
        let options = { options with Path = remoteAddress }
        (new T<_>(remoteAddress, transport, options) :> IRemoteActor<_>)
        |> Actor.logEvents options.Logger

    let spawn remoteAddress transport options = 
        create remoteAddress transport options
        |> Registry.Actor.register
        |> start    

