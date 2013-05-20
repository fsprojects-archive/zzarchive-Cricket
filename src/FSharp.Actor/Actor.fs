namespace FSharp.Actor

open System
open System.Threading
open System.Collections.Generic
open System.Collections.Concurrent
open FSharp.Actor

    type ActorBehaviour<'a> = Actor<'a> -> Async<unit>
    
    and ActorConfig<'a> = {
            Path : ActorPath.T
            RecieveTimeout : TimeSpan option
            Mailbox : IMailbox<'a>
            Behaviours : Stack<ActorBehaviour<'a>>
            Events : Events.Actor.T
            Supervisor : Supervisor.Config
            Children : ResizeArray<ActorRef>
        }
        with
            static member Create(?path, ?timeout, ?eventStream, ?mailbox : IMailbox<_>, ?behaviours:seq<_>, ?supervisor, ?supervisorStrategy, ?events, ?children) = 
                { 
                  Path = defaultArg path (ActorPath.create (Guid.NewGuid().ToString()))
                  RecieveTimeout = timeout ;  
                  Mailbox = defaultArg mailbox (new DefaultMailbox<'a>() :> IMailbox<'a>); 
                  Behaviours = (new Stack<_>(defaultArg behaviours Seq.empty))
                  Events = defaultArg events Events.Actor.Default
                  Supervisor = Supervisor.Config.Empty
                  Children = (new ResizeArray<ActorRef>())
                }

    and Actor<'a> internal(config:ActorConfig<'a>) as self =
        inherit ActorRef(config.Path)

        let mutable cts : CancellationTokenSource = null
        let mutable state = Shutdown
        let mutable systemMailbox = Unchecked.defaultof<IMailbox<SystemMessage>>
        let bag = new Dictionary<string, obj>()

        let shutDown(actor:Actor<_>) = 
             Events.Actor.run actor.Configuration.Events.PreStop (actor.Ref())
             cts.Cancel()
             systemMailbox.Dispose()
             state <- Shutdown
             cts <- null
             Kernel.Actor.remove actor
             Events.Actor.run actor.Configuration.Events.OnStopped (actor.Ref())

        let rec working(actor:Actor<'a>) = async {
            while not <| cts.IsCancellationRequested && (state = OK) do
                try
                    if actor.Configuration.Behaviours.Count > 0
                    then do! (actor.Configuration.Behaviours.Peek() actor)
                    else 
                        do! actor.Receive() |> Async.Ignore //Throw away messages until we have a behaviour
                with e ->
                    state <- ActorStatus.Errored
                    match config.Supervisor.Notify (Errored(actor, e)) with
                    | Some _ -> do! errored actor
                    | None -> 
                        actor.Log.Error(sprintf "Actor %A errored shutting down" actor, e)
                        do shutDown(actor)
            }

        and errored(actor:Actor<'a>) = async {
             while not <| cts.IsCancellationRequested do
                try
                    let! sysMsg = systemMailbox.Receive(None, cts.Token)
                    match sysMsg with
                    //Ignore supervisor messages in this state we are in trouble ourselves here
                    | Supervisor(msg) -> () 
                    | Restart -> do actor.Restart()
                    | Die -> do shutDown(actor)
                with e -> 
                    actor.Log.Error(sprintf "%A failed handling system message" actor, e)
            }

        let start'(actor:Actor<_>) = 
            cts <- new CancellationTokenSource()
            systemMailbox <- new DefaultMailbox<SystemMessage>() :> IMailbox<SystemMessage>
            state <- OK
            Async.Start(working(actor), cts.Token)
            

        let start(actor:Actor<_>) =
            Events.Actor.run actor.Configuration.Events.PreStart (actor.Ref())
            start'(actor)
            Events.Actor.run actor.Configuration.Events.OnStarted (actor.Ref())

        let restart(actor:Actor<_>) =
            shutDown(actor)
            state <- Restarting
            Events.Actor.run actor.Configuration.Events.PreRestart (actor.Ref())
            start'(actor)
            Events.Actor.run actor.Configuration.Events.OnRestarted (actor.Ref())

        do 
            start(self)

        override x.Post(message) = 
            if state <> Shutdown && state <> Restarting
            then
                match message with
                | :? SystemMessage as a when state = OK ->
                    match a with
                    | Die -> shutDown(x)
                    | Restart -> x.Restart()
                    | Supervisor(sup) -> x.Configuration.Supervisor.Handle(x,sup)
                | :? SystemMessage as a -> systemMailbox.Post(a)
                | _ -> config.Mailbox.Post(message :?> 'a)
            else invalidOp (sprintf "Actor (%A) could not handle message, State: %A" x x.Status)  
        
        member x.Receive() = 
            async {
                return! x.Configuration.Mailbox.Receive(Option.map (fun (x:TimeSpan) -> x.TotalMilliseconds |> int) x.Configuration.RecieveTimeout, cts.Token)
            }
        
        override x.QueueLength with get() = config.Mailbox.Length
        
        member internal x.Bag with get() = bag

        static member (?<-) (actor:Actor<'a>,key:string,value:obj) = 
            actor.Bag.[key] <- value
        static member (?) (actor:Actor<'a>,key:string) : 'b =
            match actor.Bag.TryGetValue(key) with
            | true, v -> v :?> 'b
            | false, _ -> Unchecked.defaultof<'b>


        member x.Configuration with get() : ActorConfig<'a> = config
        member x.Behave(reciever) = config.Behaviours.Push(reciever)
        member x.UnBehave() = config.Behaviours.Pop() |> ignore
        member x.Log with get() : Logging.Adapter = Kernel.Logger
        override x.Watch(supervisor) = 
            x.Log.Debug(sprintf "%A Set supervisor %A" x supervisor)
            config.Supervisor.Watch(x, supervisor)
        override x.Unwatch() = 
            x.Log.Debug(sprintf "%A Removed supervisor" x)
            config.Supervisor.Unwatch(x)
        override x.Link(actorRef) = 
            x.Log.Debug(sprintf "%A Linked to %A" x actorRef)
            config.Children.Add(actorRef)
        override x.Unlink(actorRef) = 
            x.Log.Debug(sprintf "%A unlinked %A" x actorRef)
            config.Children.Remove(actorRef) |> ignore
        override x.Status with get() : ActorStatus = state
        override x.Children with get() = config.Children.AsReadOnly() :> seq<_>
        member x.Ref() = x :> ActorRef
        member internal x.Restart() = restart(x)
       


    type NullActor(?id) =
        inherit ActorRef(ActorPath.T(?id = id))

        override x.QueueLength with get() = 0
        override x.Post(m) = ()
        override x.Watch(supervisor) = ()
        override x.Unwatch() = ()
        override x.Link(actorRef) = ()
        override x.Unlink(actorRef) = ()
        override x.Status with get() : ActorStatus = ActorStatus.OK
        override x.Children with get() = Seq.empty
        member x.Ref() = x :> ActorRef
