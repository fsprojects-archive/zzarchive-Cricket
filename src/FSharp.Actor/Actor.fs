namespace FSharp.Actor 

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Diagnostics

#if INTERACTIVE
open FSharp.Actor
open FSharp.Actor.Diagnostics
#endif
    
type ActorConfiguration<'a> = {
    Path : ActorPath
    EventStream : IEventStream option
    Behaviour : MessageHandler<ActorCell<'a>, unit>
    Mailbox : IMailbox<Message<'a>> option
    OnError : (exn -> Async<unit>) option
    MaxQueueLength : int option
    AwaitFirstMessage : bool
}
with
    override x.ToString() = "Config: " + x.Path.ToString()
             
type Actor<'a>(defn:ActorConfiguration<'a>) as self = 
    let metricContext = Metrics.createContext (defn.Path.Path)
    let shutdownCounter = Metrics.createCounter(metricContext,"shutdownCount")
    let errorCounter = Metrics.createCounter(metricContext,"errorCount")
    let restartCounter = Metrics.createCounter(metricContext,"restartCount")
    let uptimer = Metrics.createUptime(metricContext,"uptime", 1000)

    let firstArrivalGate = new ManualResetEventSlim(not defn.AwaitFirstMessage)
    let mailbox = defaultArg defn.Mailbox (new DefaultMailbox<Message<'a>>(metricContext.Key + "/mailbox", ?boundingCapacity = defn.MaxQueueLength) :> IMailbox<_>)
    let systemMailbox = new DefaultMailbox<Message<SystemMessage>>(metricContext.Key + "/system_mailbox") :> IMailbox<_>
    let defn = defn
    let ctx = ActorCell<'a>.Create(self, mailbox)

    let mutable cts = new CancellationTokenSource()
    let mutable messageHandlerCancel = new CancellationTokenSource()
    let mutable status = ActorStatus.Stopped

    let publishEvent event = 
        Option.iter (fun (es:IEventStream) -> es.Publish(event)) defn.EventStream

    let setStatus stats = 
        status <- stats
    
    let mutable onShutdown = (fun () -> async.Zero())
    let mutable onRestart = (fun () -> async.Zero())

    let shutdown() = 
        async {
            try
                publishEvent(ActorEvent.ActorShutdown(self.Ref))
                messageHandlerCancel.Cancel()
                setStatus ActorStatus.Stopped
                shutdownCounter(1L)
                uptimer.Stop()
                do! onShutdown()
                return ()
            with e -> 
                publishEvent(ActorEvent.ActorErrored(self.Ref, new Exception("Errored handling error", e)))
                return ()
        }

    let mutable onError = defaultArg defn.OnError (fun ctx -> async { return! shutdown() })

    let handleError (err:exn) =
        async {
            try
                setStatus(ActorStatus.Errored(err))
                publishEvent(ActorEvent.ActorErrored(self.Ref, err))
                errorCounter(1L)
                do! onError err
            with e -> 
                publishEvent(ActorEvent.ActorErrored(self.Ref, new Exception("Errored handling error", e)))
                return! shutdown()
        }

    let rec messageHandler() =
        setStatus ActorStatus.Running
        async {
            try
                uptimer.Start()
                firstArrivalGate.Wait(messageHandlerCancel.Token)
                if not(messageHandlerCancel.IsCancellationRequested)
                then do! MessageHandler.toAsync ctx defn.Behaviour 
                setStatus ActorStatus.Stopped
                return! shutdown()
            with e -> 
                do! handleError e
        }

    let rec restart() =
        async { 
            try
                publishEvent(ActorEvent.ActorRestart(self.Ref))
                restartCounter(1L)
                do messageHandlerCancel.Cancel()
                do! onRestart()
                uptimer.Reset()
                do start()
                return! systemMessageHandler()
            with e -> 
                publishEvent(ActorEvent.ActorErrored(self.Ref, new Exception("Errored restarting", e)))
                return! shutdown()
        }

    and systemMessageHandler() = 
        async {
            let! sysMsg = systemMailbox.Receive()
            match sysMsg.Message with
            | Shutdown -> return! shutdown()
            | Restart -> return! restart()
            | Link -> 
                onError <- (fun err -> MessageHandler.toAsync ctx (Message.post sysMsg.Sender (Error(err))))
                onShutdown <- (fun () -> MessageHandler.toAsync ctx (Message.post sysMsg.Sender ChildShutdown))
                publishEvent(ActorLinked(sysMsg.Sender, ctx.Self))
                return! systemMessageHandler()
            | UnLink -> 
                onError <- (defaultArg defn.OnError (fun ctx -> async { return! shutdown() }))
                onShutdown <- (fun () -> async.Zero())
                publishEvent(ActorUnLinked(sysMsg.Sender, ctx.Self))
                return! systemMessageHandler()
        }

    and start() = 
        if messageHandlerCancel <> null
        then
            messageHandlerCancel.Dispose()
            messageHandlerCancel <- null
        messageHandlerCancel <- new CancellationTokenSource()
        Async.Start(async {
                        publishEvent(ActorEvent.ActorStarted(self.Ref))
                        do! messageHandler()
                    }, messageHandlerCancel.Token)

    do 
        Async.Start(systemMessageHandler(), cts.Token)
        start()
   
    override __.ToString() = defn.Path.ToString()

    member x.Ref = ActorRef(x)

    interface IActor with
        member x.Path with get() = defn.Path
        member x.Post(msg) =
            if status <> ActorStatus.Stopped
            then
               match msg.Message with
               | :? SystemMessage -> systemMailbox.Post(Message.map unbox msg)
               | _ -> (x :> IActor<'a>).Post(Message.map unbox msg)

    interface IActor<'a> with
        member x.Path with get() = defn.Path
        member x.Post(msg) =
            if status <> ActorStatus.Stopped
            then
                if not(firstArrivalGate.IsSet) then firstArrivalGate.Set()
                mailbox.Post(msg) 

    interface IDisposable with  
        member x.Dispose() =
            messageHandlerCancel.Dispose()
            cts.Dispose()

[<AutoOpen>]
module ActorConfiguration = 
    
    let messageHandler = new Message.MessageHandlerBuilder()

    type ActorConfigurationBuilder internal() = 
        member __.Zero() = { 
            Path = ActorPath.ofString (Guid.NewGuid().ToString())
            EventStream = None
            Behaviour = MessageHandler.empty
            OnError = None
            MaxQueueLength = Some 1000000
            Mailbox = None
            AwaitFirstMessage = true
        }
        member x.Yield(()) = x.Zero()
        [<CustomOperation("inherits", MaintainsVariableSpace = true)>]
        member __.Inherits(_, b:ActorConfiguration<'a>) = b
        [<CustomOperation("path", MaintainsVariableSpace = true)>]
        member __.Path(ctx, name) = 
            {ctx with Path = name }
        [<CustomOperation("name", MaintainsVariableSpace = true)>]
        member __.Name(ctx, name) = 
            {ctx with Path = ActorPath.ofString name }
        [<CustomOperation("maxQueueLength", MaintainsVariableSpace = true)>]
        member __.MaxQueueLength(ctx, length) = 
            { ctx with MaxQueueLength = Some length }
        [<CustomOperation("mailbox", MaintainsVariableSpace = true)>]
        member __.Mailbox(ctx:ActorConfiguration<'a>, mailbox) = 
            {ctx with Mailbox = mailbox }
        [<CustomOperation("body", MaintainsVariableSpace = true)>]
        member __.Body(ctx, behaviour) = 
            { ctx with Behaviour = behaviour }
        [<CustomOperation("raiseEventsOn", MaintainsVariableSpace = true)>]
        member __.RaiseEventsOn(ctx:ActorConfiguration<'a>, es) = 
            { ctx with EventStream = Some es }
        [<CustomOperation("waitFirstMessage", MaintainsVariableSpace = true)>]
        member __.WaitFirstMessage(ctx:ActorConfiguration<'a>, es) = 
            { ctx with AwaitFirstMessage = es }

    let actor = new ActorConfigurationBuilder()


module Actor = 
    
    let start (config:ActorConfiguration<'a>) =
        let actor = new Actor<'a>(config)
        ActorRef(actor)

    let register ref =
        ActorHost.Instance.RegisterActor ref
        ref

    let spawn (config:ActorConfiguration<'a>) =
        let config = {
            config with
                EventStream = Some ActorHost.Instance.EventStream
                Path = ActorPath.setHost ActorHost.Instance.Name config.Path
        }

        config |> (start >> register)

type SupervisorConfiguration = {
    Path : ActorPath
    Children : ActorSelection
    Behaviour : (exn * ActorSelection * ActorSelection -> MessageHandler<ActorCell<SupervisorMessage>,unit>)
}

module Supervisor =
    
    let fail (_,sender,_) = Message.post sender SystemMessage.Shutdown

    let failAll (_,_,children) = Message.post children SystemMessage.Shutdown

    let oneForOne (_,sender,_) = Message.post sender SystemMessage.Restart

    let oneForAll (_,_,children) = Message.post children SystemMessage.Restart

    let toActor (config : SupervisorConfiguration) =
        actor {
            path config.Path
            waitFirstMessage false
            body (
                let rec loop children = 
                    messageHandler {
                        let! msg = Message.receive()
                        let! sender = Message.sender()
                        match msg with
                        | Error(err) ->
                            do! config.Behaviour (err, sender, children)
                            return! loop children
                        | ChildShutdown ->
                            return! loop (ActorSelection.exclude sender children)
                        | ChildRestart -> 
                            return! loop children
                        | ChildLink ->
                            do! Message.post sender Link
                            return! loop (ActorSelection.combine sender children)
                        | ChildUnLink ->
                            do! Message.post sender UnLink
                            return! loop (ActorSelection.exclude sender children)   
                    }
                messageHandler {
                    do! Message.post config.Children Link
                    return! loop config.Children
                }    
            )
        }

    let start (config : SupervisorConfiguration) = 
        toActor config |> Actor.start

    let spawn (config : SupervisorConfiguration) =
        toActor config |> Actor.spawn

    let link ref supervisor = 
        Message.postMessage supervisor (Message.create (Some ref) ChildLink)

    let unlink ref supervisor =
        Message.postMessage supervisor (Message.create (Some ref) ChildUnLink)


[<AutoOpen>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SupervisorConfiguration = 

    type SupervisorConfigurationBuilder internal() = 
        member __.Zero() = 
            { 
                Path = ActorPath.ofString (Guid.NewGuid().ToString())
                Children = ActorSelection.empty
                Behaviour = Supervisor.oneForOne
            }
        member x.Yield(()) = x.Zero()
        [<CustomOperation("path", MaintainsVariableSpace = true)>]
        member __.Path(ctx:SupervisorConfiguration, name) = 
            {ctx with Path = name }
        [<CustomOperation("name", MaintainsVariableSpace = true)>]
        member __.Name(ctx:SupervisorConfiguration, name) = 
            {ctx with Path = ActorPath.ofString name }
        [<CustomOperation("link", MaintainsVariableSpace = true)>]
        member __.Link(ctx:SupervisorConfiguration, children) = 
            {ctx with Children = (ActorSelection.op_Implicit children) }
        [<CustomOperation("strategy", MaintainsVariableSpace = true)>]
        member __.Body(ctx:SupervisorConfiguration, behaviour) = 
            { ctx with Behaviour = behaviour }

    let supervisor = new SupervisorConfigurationBuilder()