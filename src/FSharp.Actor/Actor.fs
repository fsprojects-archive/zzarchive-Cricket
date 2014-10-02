namespace FSharp.Actor 

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Diagnostics

#if INTERACTIVE
open FSharp.Actor
open FSharp.Actor.Diagnostics
#endif
    
type ActorConfiguration<'a,'b> = {
    Path : ActorPath
    EventStream : IEventStream option
    Parent : ActorRef
    Children : ActorRef list
    SupervisorStrategy : (ErrorContext -> unit)
    Behaviour : MessageHandler<'a, 'b>
    Mailbox : IMailbox<Message<'a>> option
    MaxQueueLength : int option
}
with
    override x.ToString() = "Config: " + x.Path.ToString()
             
type Actor<'a, 'b>(defn:ActorConfiguration<'a, 'b>) as self = 
    let metricContext = Metrics.createContext (defn.Path.Path)
    let shutdownCounter = Metrics.createCounter(metricContext,"shutdownCount")
    let errorCounter = Metrics.createCounter(metricContext,"errorCount")
    let restartCounter = Metrics.createCounter(metricContext,"restartCount")
    let uptimer = Metrics.createUptime(metricContext,"uptime", 1000)

    let mailbox = defaultArg defn.Mailbox (new DefaultMailbox<Message<'a>>(metricContext.Key + "/mailbox", ?boundingCapacity = defn.MaxQueueLength) :> IMailbox<_>)
    let systemMailbox = new DefaultMailbox<SystemMessage>(metricContext.Key + "/system_mailbox") :> IMailbox<_>
    let firstArrivalGate = new ManualResetEventSlim(false)

    let mutable cts = new CancellationTokenSource()
    let mutable messageHandlerCancel = new CancellationTokenSource()
    let mutable defn = defn
    let mutable ctx = { Self = self; Mailbox = mailbox; Children = defn.Children; ParentId = None; SpanId = 0UL; Sender = Null; }
    let mutable status = ActorStatus.Stopped

    let publishEvent event = 
        Option.iter (fun (es:IEventStream) -> es.Publish(event)) defn.EventStream

    let setStatus stats = 
        status <- stats

    let shutdown includeChildren = 
        async {
            publishEvent(ActorEvent.ActorShutdown(self.Ref))
            messageHandlerCancel.Cancel()
            
            if includeChildren
            then Seq.iter (fun (t:ActorRef) -> t.Post(Shutdown, self.Ref)) ctx.Children

            setStatus ActorStatus.Stopped
            shutdownCounter(1L)
            uptimer.Stop()
            return ()
        }

    let handleError (err:exn) =
        async {
            setStatus(ActorStatus.Errored(err))
            publishEvent(ActorEvent.ActorErrored(self.Ref, err))
            errorCounter(1L)
            match defn.Parent with
            | Null -> return! shutdown true 
            | _ as actor -> actor.Post(Errored({ Error = err; Sender = self.Ref; Children = ctx.Children }),self.Ref)
        }

    let rec messageHandler() =
        setStatus ActorStatus.Running
        async {
            try
                uptimer.Start()
                firstArrivalGate.Wait(messageHandlerCancel.Token)
                if not(messageHandlerCancel.IsCancellationRequested)
                then do! Message.toAsync defn.Behaviour ctx
                setStatus ActorStatus.Stopped
                return! shutdown true
            with e -> 
                do! handleError e
        }

    let rec restart includeChildren =
        async { 
            publishEvent(ActorEvent.ActorRestart(self.Ref))
            restartCounter(1L)
            do messageHandlerCancel.Cancel()

            if includeChildren
            then Seq.iter (fun (t:ActorRef) -> t.Post(Restart, self.Ref)) ctx.Children
            uptimer.Reset()
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
               match ref with
               | ActorRef(a) when defn.Parent <> ref -> 
                    defn.Parent.Post(Link(self.Ref), self.Ref)
                    defn <- { defn with Parent =  ref }
               | ActorRef(_) -> ()
               | Null -> 
                    defn.Parent.Post(Unlink(self.Ref), self.Ref)
                    defn <- { defn with Parent =  ref }
               return! systemMessageHandler()
            | RemoveParent(ref) ->
               match ref with
               | ActorRef(a) when defn.Parent = ref -> 
                    defn.Parent.Post(Unlink(self.Ref), self.Ref)
                    defn <- { defn with Parent =  Null }
               | ActorRef(_) -> ()
               | Null -> 
                    defn.Parent.Post(Unlink(self.Ref), self.Ref)
                    defn <- { defn with Parent =  Null }
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
        ctx.Children |> List.iter (fun t -> t.Post(SetParent(self.Ref), self.Ref))
        start()
   
    override __.ToString() = defn.Path.ToString()

    member x.Ref = ActorRef(x)

    interface IActor with
        member x.Path with get() = defn.Path
        member x.Post(msg) =
            if status <> ActorStatus.Stopped
            then
               match msg.Message with
               | :? SystemMessage as msg -> systemMailbox.Post(msg)
               | _ -> (x :> IActor<'a>).Post(Message<'a>.Unbox(msg))

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
            SupervisorStrategy = (fun x -> x.Sender.Post(Shutdown, x.Sender));
            Parent = Null;
            Children = []; 
            Behaviour = Message.emptyHandler
            MaxQueueLength = Some 1000000
            Mailbox = None  }
        member x.Yield(()) = x.Zero()
        [<CustomOperation("inherits", MaintainsVariableSpace = true)>]
        member __.Inherits(_:ActorConfiguration<'a,'b>, b:ActorConfiguration<_,_>) = b
        [<CustomOperation("path", MaintainsVariableSpace = true)>]
        member __.Path(ctx:ActorConfiguration<'a,'b>, name) = 
            {ctx with Path = name }
        [<CustomOperation("name", MaintainsVariableSpace = true)>]
        member __.Name(ctx:ActorConfiguration<'a,'b>, name) = 
            {ctx with Path = ActorPath.ofString name }
        [<CustomOperation("maxQueueLength", MaintainsVariableSpace = true)>]
        member __.MaxQueueLength(ctx:ActorConfiguration<'a,'b>, length) = 
            { ctx with MaxQueueLength = Some length }
        [<CustomOperation("mailbox", MaintainsVariableSpace = true)>]
        member __.Mailbox(ctx:ActorConfiguration<'a,'b>, mailbox) = 
            {ctx with Mailbox = mailbox }
        [<CustomOperation("body", MaintainsVariableSpace = true)>]
        member __.Body(ctx:ActorConfiguration<'a,'b>, behaviour) = 
            { ctx with Behaviour = behaviour }
        [<CustomOperation("parent", MaintainsVariableSpace = true)>]
        member __.SupervisedBy(ctx:ActorConfiguration<'a,'b>, sup) = 
            { ctx with Parent = sup }
        [<CustomOperation("children", MaintainsVariableSpace = true)>]
        member __.Children(ctx:ActorConfiguration<'a,'b>, children) =
            { ctx with Children = children }
        [<CustomOperation("supervisorStrategy", MaintainsVariableSpace = true)>]
        member __.SupervisorStrategy(ctx:ActorConfiguration<'a,'b>, supervisorStrategy) = 
            { ctx with SupervisorStrategy = supervisorStrategy }
        [<CustomOperation("raiseEventsOn", MaintainsVariableSpace = true)>]
        member __.RaiseEventsOn(ctx:ActorConfiguration<'a,'b>, es) = 
            { ctx with EventStream = Some es }

    let actor = new ActorConfigurationBuilder()


module Actor = 
    
    let start (config:ActorConfiguration<'a,'b>) =
        let actor = new Actor<'a,'b>(config)
        ActorRef(actor)

    let register ref =
        ActorHost.Instance.RegisterActor ref
        ref

    let spawn (config:ActorConfiguration<'a,'b>) =
        let config = {
            config with
                EventStream = Some ActorHost.Instance.EventStream
                Path = ActorPath.setHost ActorHost.Instance.Name config.Path
        }

        config |> (start >> register)

[<AutoOpen>]
module ActorOperators =
 
    let inline (!!) a = ActorSelection.op_Implicit a
    
    let inline (!~) a = lazy !!a  
    
    let inline (-->) msg t = 
        Message.postMessage t { Id = Some (Random.randomLong()); Sender = Null; Message = msg }
    
    let inline (<--) t msg =
        Message.postMessage t { Id = Some (Random.randomLong()); Sender = Null; Message = msg }