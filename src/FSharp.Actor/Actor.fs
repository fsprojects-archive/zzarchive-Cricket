namespace FSharp.Actor 

open System
open System.Threading
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open System.Runtime.Remoting.Messaging
open FSharp.Actor

#if INTERACTIVE
open FSharp.Actor
#endif



type Message<'a> = {
    Sender : actorRef
    Target : actorPath
    Message : 'a
}

type ActorEvent = 
    | ActorStarted of actorRef
    | ActorShutdown of actorRef
    | ActorRestart of actorRef
    | ActorErrored of actorRef * exn
    | ActorAddedChild of actorRef * actorRef
    | ActorRemovedChild of actorRef * actorRef

type ActorStatus = 
    | Running 
    | Errored of exn
    | Stopped

type ErrorContext = {
    Error : exn
    Sender : actorRef
    Children : actorRef list
} 

type SystemMessage =
    | Shutdown
    | RestartTree
    | Restart
    | Link of actorRef
    | Unlink of actorRef
    | SetParent of actorRef
    | Errored of ErrorContext

type ActorLogger(path:actorPath, logger : Log.ILogger) =
    inherit Log.Logger(path.ToString(), logger)


type ActorCell<'a> = {
    Logger : ActorLogger
    Children : actorRef list
    Mailbox : IMailbox<Message<'a>>
    Self : actorRef
}
with 
    member x.TryReceive(?timeout) = 
        async { return! x.Mailbox.TryReceive(defaultArg timeout Timeout.Infinite) }
    member x.Receive(?timeout) = 
        async { return! x.Mailbox.Receive(defaultArg timeout Timeout.Infinite) }
    member x.TryScan(f, ?timeout) = 
        async { return! x.Mailbox.TryScan(defaultArg timeout Timeout.Infinite, f) }
    member x.Scan(f, ?timeout) = 
        async { return! x.Mailbox.Scan(defaultArg timeout Timeout.Infinite, f) }
    member internal x.Path = ActorRef.path x.Self

type ActorConfiguration<'a> = {
    Path : actorPath
    EventStream : IEventStream option
    Parent : actorRef
    Children : actorRef list
    SupervisorStrategy : (ErrorContext -> unit)
    Behaviour : (ActorCell<'a> -> Async<unit>)
    Mailbox : IMailbox<Message<'a>> option
    Logger : Log.ILogger
}
with
    override x.ToString() = "Config: " + x.Path.ToString()
                    
type Actor<'a>(defn:ActorConfiguration<'a>) as self = 
    let mailbox = defaultArg defn.Mailbox (new DefaultMailbox<Message<'a>>() :> IMailbox<_>)
    let systemMailbox = new DefaultMailbox<SystemMessage>() :> IMailbox<_>
    let logger = new ActorLogger(defn.Path, defn.Logger)
    let firstArrivalGate = new ManualResetEventSlim() 

    let mutable cts = new CancellationTokenSource()
    let mutable messageHandlerCancel = new CancellationTokenSource()
    let mutable defn = defn
    let mutable ctx = { Self = ActorRef(self); Mailbox = mailbox; Logger = logger; Children = defn.Children; }
    let mutable status = ActorStatus.Stopped


    let publishEvent event = 
        Option.iter (fun (es:IEventStream) -> es.Publish(event)) defn.EventStream

    let setStatus stats = 
        status <- stats

    let shutdown includeChildren = 
        async {
            publishEvent(ActorEvent.ActorShutdown(ctx.Self))
            messageHandlerCancel.Cancel()
            
            if includeChildren
            then Seq.iter (fun t -> post t Shutdown) ctx.Children

            match status with
            | ActorStatus.Errored(err) -> logger.Debug("shutdown", exn = err)
            | _ -> logger.Debug("shutdown")
            setStatus ActorStatus.Stopped
            return ()
        }

    let handleError (err:exn) =
        async {
            setStatus(ActorStatus.Errored(err))
            publishEvent(ActorEvent.ActorErrored(ctx.Self, err))
            match defn.Parent with
            | ActorRef(actor) -> 
                actor.Post(Errored({ Error = err; Sender = ctx.Self; Children = ctx.Children }),ctx.Self)
                return ()
            | Null -> return! shutdown true 
        }

    let rec messageHandler() =
        setStatus ActorStatus.Running
        async {
            try
                firstArrivalGate.Wait(messageHandlerCancel.Token)
                if not(messageHandlerCancel.IsCancellationRequested)
                then do! defn.Behaviour ctx
                setStatus ActorStatus.Stopped
                publishEvent(ActorEvent.ActorShutdown ctx.Self)
            with e -> 
                do! handleError e
        }

    let rec restart includeChildren =
        async { 
            publishEvent(ActorEvent.ActorRestart(ctx.Self))
            do messageHandlerCancel.Cancel()

            if includeChildren
            then Seq.iter (fun t -> post t Restart) ctx.Children

            match status with
            | ActorStatus.Errored(err) -> logger.Debug("restarted", exn = err)
            | _ -> logger.Debug("restarted")
            do start()
            return! systemMessageHandler()
        }

    and systemMessageHandler() = 
        async {
            let! sysMsg = systemMailbox.Receive(Timeout.Infinite)
            match sysMsg with
            | Shutdown -> return! shutdown true
            | Restart -> return! restart false
            | RestartTree -> return! restart true
            | Errored(errContext) -> 
                defn.SupervisorStrategy(errContext) 
                return! systemMessageHandler()
            | Link(ref) -> 
                ctx <- { ctx with Children = (ref :: ctx.Children) }
                return! systemMessageHandler()
            | Unlink(ref) -> 
                ctx <- { ctx with Children = (List.filter ((<>) ref) ctx.Children) }
                return! systemMessageHandler()
            | SetParent(ref) ->
               match ref, defn.Parent with
               | Null, Null -> ()
               | ActorRef(a), ActorRef(a') when a' = a -> ()
               | Null, _ -> 
                    defn.Parent <-- Unlink(ctx.Self)
                    defn <- { defn with Parent =  ref }
               | _, Null -> 
                    defn.Parent <-- Link(ctx.Self)
                    defn <- { defn with Parent =  ref }
               | ActorRef(a), ActorRef(a') ->
                    defn.Parent <-- Unlink(ctx.Self)
                    ref <-- Link(ctx.Self)
                    defn <- { defn with Parent =  ref }
               return! systemMessageHandler()
        }

    and start() = 
        if messageHandlerCancel <> null
        then
            messageHandlerCancel.Dispose()
            messageHandlerCancel <- null
        messageHandlerCancel <- new CancellationTokenSource()
        Async.Start(async {
                        CallContext.LogicalSetData("actor", ctx.Self)
                        publishEvent(ActorEvent.ActorStarted(ctx.Self))
                        do! messageHandler()
                    }, messageHandlerCancel.Token)

    do 
        Async.Start(systemMessageHandler(), cts.Token)
        ctx.Children |> List.iter ((-->) (SetParent(ctx.Self)))
        start()
   
    override x.ToString() = defn.Path.ToString()

    interface IActor with
        member x.Path with get() = defn.Path
        member x.Post(msg, sender) =
               match msg with
               | :? SystemMessage as msg -> systemMailbox.Post(msg)
               | msg -> (x :> IActor<'a>).Post(unbox<'a> msg, sender)

    interface IActor<'a> with
        member x.Path with get() = defn.Path
        member x.Post(msg:'a, sender) =
             if not(firstArrivalGate.IsSet) then firstArrivalGate.Set()
             mailbox.Post({Target = ctx.Path; Sender = sender; Message = msg}) 

    interface IDisposable with  
        member x.Dispose() =
            messageHandlerCancel.Dispose()
            cts.Dispose()

type RemoteMessage = {
    Target : actorPath
    Sender : actorPath
    Message : obj
}

type ITransport =
    inherit IDisposable
    abstract Scheme : string with get
    abstract BasePath : actorPath with get
    abstract Post : actorPath * RemoteMessage -> unit
    abstract Start : ISerializer * CancellationToken -> unit

type RemoteActor(path:actorPath, transport:ITransport) =
    override x.ToString() = path.ToString()

    interface IActor with
        member x.Path with get() = path
        member x.Post(msg, sender) =
            transport.Post(path, { Target = path; Sender = ActorPath.rebase transport.BasePath (sender |> ActorRef.path); Message = msg })
        member x.Dispose() = ()
        

[<AutoOpen>]
module ActorConfiguration = 
    

    type ActorConfigurationBuilder internal() = 
        member x.Zero() = { 
            Path = ActorPath.ofString (Guid.NewGuid().ToString()); 
            EventStream = None
            SupervisorStrategy = (fun x -> x.Sender <-- Shutdown);
            Parent = Null;
            Children = []; 
            Behaviour = (fun ctx -> 
                 let rec loop() =
                      async { return! loop() }
                 loop()
            )
            Logger = Log.defaultFor Log.Debug
            Mailbox = None  }
        member x.Yield(()) = x.Zero()
        [<CustomOperation("inherits", MaintainsVariableSpace = true)>]
        member x.Inherits(ctx:ActorConfiguration<'a>, b:ActorConfiguration<_>) = b
        [<CustomOperation("path", MaintainsVariableSpace = true)>]
        member x.Path(ctx:ActorConfiguration<'a>, name) = 
            {ctx with Path = name }
        [<CustomOperation("name", MaintainsVariableSpace = true)>]
        member x.Name(ctx:ActorConfiguration<'a>, name) = 
            {ctx with Path = ActorPath.ofString name }
        [<CustomOperation("mailbox", MaintainsVariableSpace = true)>]
        member x.Mailbox(ctx:ActorConfiguration<'a>, mailbox) = 
            {ctx with Mailbox = mailbox }
        [<CustomOperation("messageHandler", MaintainsVariableSpace = true)>]
        member x.MsgHandler(ctx:ActorConfiguration<'a>, behaviour) = 
            { ctx with Behaviour = behaviour }
        [<CustomOperation("parent", MaintainsVariableSpace = true)>]
        member x.SupervisedBy(ctx:ActorConfiguration<'a>, sup) = 
            { ctx with Parent = sup }
        [<CustomOperation("children", MaintainsVariableSpace = true)>]
        member x.Children(ctx:ActorConfiguration<'a>, children) =
            { ctx with Children = children }
        [<CustomOperation("supervisorStrategy", MaintainsVariableSpace = true)>]
        member x.SupervisorStrategy(ctx:ActorConfiguration<'a>, supervisorStrategy) = 
            { ctx with SupervisorStrategy = supervisorStrategy }
        [<CustomOperation("raiseEventsOn", MaintainsVariableSpace = true)>]
        member x.RaiseEventsOn(ctx:ActorConfiguration<'a>, es) = 
            { ctx with EventStream = Some es }
        [<CustomOperation("Logger", MaintainsVariableSpace = true)>]
        member x.Logger(ctx:ActorConfiguration<'a>, logger) = 
            { ctx with Logger = logger }

    let actor = new ActorConfigurationBuilder()
