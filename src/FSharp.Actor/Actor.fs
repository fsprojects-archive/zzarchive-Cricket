namespace FSharp.Actor

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Types

module Actor = 

    type Options<'a> = {
        Id : string
        Mailbox : IMailbox<ActorMessage<'a>>
        Supervisor : IActor<SupervisorMessage> option
        Logger : ILogger
        Path : ActorPath
    }
    with 
        static member Create(?id, ?mailbox, ?supervisor, ?logger, ?address) = 
            let logger = defaultArg logger Logging.Console
            let id = defaultArg id (Guid.NewGuid().ToString())
            {
                Id = id
                Mailbox = defaultArg mailbox (new DefaultMailbox<ActorMessage<'a>>() :> IMailbox<ActorMessage<'a>>)
                Supervisor = supervisor
                Logger = logger
                Path = defaultArg address (Path.create id)
            }
        static member Default = Options<'a>.Create()
    
    type T<'a>internal(computation : IActor<'a> -> Async<unit>, ?options) as self =
        let mutable status = ActorStatus.Shutdown("")
        let mutable cts = new CancellationTokenSource()  
        let mutable options = defaultArg options (Options<'a>.Create())
        let preStart = new Event<IActor>()
        let preRestart = new Event<IActor>()
        let preStop = new Event<IActor>()
        let onStopped = new Event<IActor>()
        let onRestarted = new Event<IActor>()
        let onStart = new Event<IActor>()
        let children = new ResizeArray<IActor>()
        let stateChangeSync = new obj()
    
        let setStatus newStatus = 
            status <- newStatus
    
        let rec run (actor:IActor<'a>) = 
           setStatus ActorStatus.Running
           async {
                 try
                    do! computation actor
                 with e ->
                    do! handleError actor e
           }

        and handleError (actor:IActor<'a>) (err:exn) =
            setStatus <| ActorStatus.Errored(err)
            async {
                match options.Supervisor with
                | Some(supervisor) -> 
                    supervisor.Post(SupervisorMessage.ActorErrored(err, actor), None)
                | None ->
                    shutdown actor (sprintf "An exception was handled\r\n%A" err)
                return ()
            }
    
        and shutdown (actor:IActor<'a>) reason =
            lock stateChangeSync (fun _ ->
                preStop.Trigger(actor :> IActor)
                cts.Cancel()
                setStatus <| ActorStatus.Shutdown(reason)
                cts <- null
                onStopped.Trigger(actor :> IActor)
                )
    
        and start (actor:IActor<'a>) reason = 
            lock stateChangeSync (fun _ ->
                preStart.Trigger(actor :> IActor)  
                cts <- new CancellationTokenSource()
                Async.Start(run self, cts.Token)
                onStart.Trigger(actor :> IActor)
            )
    
        and restart (actor:IActor<'a>) reason =
            lock stateChangeSync (fun _ ->
                preRestart.Trigger(actor :> IActor)
                shutdown actor (sprintf "Restarting: %s" reason)
                setStatus ActorStatus.Restarting
                start actor reason
                onRestarted.Trigger(actor :> IActor)
            )
        
        override x.ToString() =  (x :> IActor).Id
        override x.Equals(y:obj) = 
            match y with
            | :? IActor as y -> (x :> IActor).Id = y.Id
            | _ -> false
        override x.GetHashCode() = (x :> IActor).Id.GetHashCode()
    
        member x.Log with get() = options.Logger
        
        interface IActor<'a> with
            
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
            member x.QueueLength with get() = options.Mailbox.Length
            member x.Start() = 
                start self "Initial Startup"

            member x.Post(msg : obj, ?sender:IActor) = (x :> IActor<'a>).Post(msg :?> 'a, sender)
            member x.Post(msg : 'a, ?sender) =
                if status = ActorStatus.Running
                then options.Mailbox.Post(Message(msg, sender))
                else failwithf "Cannot send message actor status invalid %A" status
    
            member x.Post(msg : 'a) = (x :> IActor<'a>).Post(msg, Option<IActor>.None)
            

            member x.PostSystemMessage(sysMessage : SystemMessage, ?sender : IActor) =
                   match sysMessage with
                   | Shutdown(reason) -> shutdown x reason
                   | Restart(reason) -> restart x reason
                   
    
            member x.Receive(?timeout) = 
                async {
                    let! msg = options.Mailbox.Receive(timeout, cts.Token)
                    match msg with
                    | Message(msg, sender) -> return msg, sender
                }
    
            member x.Receive() = 
                async {
                    let! msg = options.Mailbox.Receive(None, cts.Token)
                    match msg with
                    | Message(msg, sender) -> return msg, sender
                }
    
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

    let logEvents (logger:ILogger) (actor:IActor) =
        actor.OnRestarted |> Event.add (fun a -> logger.Debug(sprintf "%A restarted Status: %A" a a.Status, None))
        actor.OnStarted |> Event.add (fun a -> logger.Debug(sprintf "%A started Status: %A" a a.Status, None))
        actor.OnStopped |> Event.add (fun a -> logger.Debug(sprintf "%A stopped Status: %A" a a.Status, None))
        actor.PreRestart |> Event.add (fun a -> logger.Debug(sprintf "%A pre-restart Status: %A" a a.Status, None))
        actor.PreStart |> Event.add (fun a -> logger.Debug(sprintf "%A pre-start Status: %A" a a.Status, None))
        actor.PreStop |> Event.add (fun a -> logger.Debug(sprintf "%A pre-stop Status: %A" a a.Status, None))
        actor

    let start (actor:IActor) = 
        actor.Start()
        actor
    
    let create (options:Options<_>) computation = 
        (new T<_>(computation, options) :> IActor<_>)
        |> logEvents options.Logger

    let register actor = 
        Registry.Actor.register actor 

    let spawn (options:Options<_>) computation =
        create options computation
        |> register
        |> start

    let supervisedBy sup (actor:IActor) = 
        actor.Watch(sup)
        actor

    let link (linkees:seq<IActor>) (actor:IActor) =
        Seq.iter actor.Link linkees
        actor

    let spawnLinked options linkees computation =
        link linkees (spawn options computation)

    let unlink linkees (actor:IActor) =
        linkees |> Seq.iter (fun (l:IActor) -> actor.UnLink(l))
        actor

    let unwatch (actors:seq<IActor>) = 
        actors |> Seq.iter (fun a -> a.UnWatch())
        
    let deadLetter name = 
        let computation (actor:IActor<_>) = 
            let rec loop() = 
                async {
                    let! msg = actor.Receive()
                    return! loop()
                }
            loop()
        new T<_>(computation, Options<_>.Create(name)) :> IActor<_>