namespace FSharp.Actor 

open System
open System.Threading
open System.Collections.Generic
open Microsoft.FSharp.Reflection
open System.Runtime.Remoting.Messaging
open FSharp.Actor
open FSharp.Actor.Diagnostics

#if INTERACTIVE
open FSharp.Actor
open FSharp.Actor.Diagnostics
#endif

type ActorCell<'a> = {
    Children : ActorRef list
    Mailbox : IMailbox<Message<'a>>
    Self : IActor
    mutable SpanId : uint64
    mutable CurrentMessage : Message<'a>
}
with 
    member internal x.TryReceive(?timeout) = 
        async { return! x.Mailbox.TryReceive(defaultArg timeout Timeout.Infinite) }
    member internal x.Receive(?timeout) = 
        async { return! x.Mailbox.Receive(defaultArg timeout Timeout.Infinite) }
    member internal x.TryScan(f, ?timeout) = 
        async { return! x.Mailbox.TryScan(defaultArg timeout Timeout.Infinite, f) }
    member internal x.Scan(f, ?timeout) = 
        async { return! x.Mailbox.Scan(defaultArg timeout Timeout.Infinite, f) }
    member internal x.Path = x.Self.Path

type MessageHandler<'a, 'b> = MH of (ActorCell<'a> -> Async<'b>)

module MessageHandler = 

    let emptyHandler = 
        (MH (fun _ -> 
         let rec loop() = 
             async { return! loop() }
         loop()))

    type MessageHandlerBuilder() = 
        member x.Bind(MH handler,f) =
             MH (fun context -> 
                  async {
                     let! comp = handler context
                     let (MH nextComp) = f comp
                    // Trace.Write (context.Self.Path.ToString()) (Some context.CurrentMessage.Id) (Some context.SpanId)
                     return! nextComp context
                  } 
             ) 
        member x.Bind(a:Async<_>, f) = 
            MH (fun context -> 
                async {
                     let! comp = a
                     let (MH nextComp) = f comp
                    // Trace.Write (context.Self.Path.ToString()) (Some context.CurrentMessage.Id) (Some context.SpanId)
                     return! nextComp context
                  } 
            )
        member x.Return(m) = MH(fun ctx -> m)
        member x.ReturnFrom(m) = m
        member x.Zero() = x.Return(async.Zero())
        member x.Delay(f) = x.Bind(x.Zero(), f)
        member x.Using(r,f) = MH(fun ctx -> use rr = r in let (MH g) = f rr in g ctx)
        member x.Combine(c1 : MessageHandler<_,_>, c2) = x.Bind(c1, fun () -> c2)
        member x.For(sq:seq<'a>, f:'a -> MessageHandler<'b, 'c>) = 
          let rec loop (en:System.Collections.Generic.IEnumerator<_>) = 
            if en.MoveNext() then x.Bind(f en.Current, fun _ -> loop en)
            else x.Zero()
          x.Using(sq.GetEnumerator(), loop)

        member x.While(t, f:unit -> MessageHandler<_, unit>) =
          let rec loop () = 
            if t() then x.Bind(f(), loop)
            else x.Zero()
          loop()
    
    let toAsync (MH handler) ctx = handler ctx |> Async.Ignore

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
    let mutable ctx = { Self = self; Mailbox = mailbox; Children = defn.Children; SpanId = 0UL; CurrentMessage = Unchecked.defaultof<_> }
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
                then do! MessageHandler.toAsync defn.Behaviour ctx
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
               match ref, defn.Parent with
               | Null, Null -> ()
               | ActorRef(a), ActorRef(a') when a' = a -> ()
               | Null, _ -> 
                    defn.Parent.Post(Unlink(self.Ref), self.Ref)
                    defn <- { defn with Parent =  ref }
               | _, Null -> 
                    defn.Parent.Post(Link(self.Ref), self.Ref)
                    defn <- { defn with Parent =  ref }
               | ActorRef(a), ActorRef(a') ->
                    defn.Parent.Post(Unlink(self.Ref), self.Ref)
                    ref.Post(Link(self.Ref), self.Ref)
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
                        CallContext.LogicalSetData("actor", self.Ref)
                        publishEvent(ActorEvent.ActorStarted(self.Ref))
                        do! messageHandler()
                    }, messageHandlerCancel.Token)

    do 
        Async.Start(systemMessageHandler(), cts.Token)
        ctx.Children |> List.iter (fun t -> t.Post(SetParent(self.Ref), self.Ref))
        start()
   
    override x.ToString() = defn.Path.ToString()

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
    
    let messageHandler = new MessageHandler.MessageHandlerBuilder()

    type ActorConfigurationBuilder internal() = 
        member x.Zero() = { 
            Path = ActorPath.ofString (Guid.NewGuid().ToString()); 
            EventStream = None
            SupervisorStrategy = (fun x -> x.Sender.Post(Shutdown, x.Sender));
            Parent = Null;
            Children = []; 
            Behaviour = MessageHandler.emptyHandler
            MaxQueueLength = Some 1000000
            Mailbox = None  }
        member x.Yield(()) = x.Zero()
        [<CustomOperation("inherits", MaintainsVariableSpace = true)>]
        member x.Inherits(ctx:ActorConfiguration<'a,'b>, b:ActorConfiguration<_,_>) = b
        [<CustomOperation("path", MaintainsVariableSpace = true)>]
        member x.Path(ctx:ActorConfiguration<'a,'b>, name) = 
            {ctx with Path = name }
        [<CustomOperation("name", MaintainsVariableSpace = true)>]
        member x.Name(ctx:ActorConfiguration<'a,'b>, name) = 
            {ctx with Path = ActorPath.ofString name }
        [<CustomOperation("maxQueueLength", MaintainsVariableSpace = true)>]
        member x.MaxQueueLength(ctx:ActorConfiguration<'a,'b>, length) = 
            { ctx with MaxQueueLength = Some length }
        [<CustomOperation("mailbox", MaintainsVariableSpace = true)>]
        member x.Mailbox(ctx:ActorConfiguration<'a,'b>, mailbox) = 
            {ctx with Mailbox = mailbox }
        [<CustomOperation("body", MaintainsVariableSpace = true)>]
        member x.Body(ctx:ActorConfiguration<'a,'b>, behaviour) = 
            { ctx with Behaviour = behaviour }
        [<CustomOperation("parent", MaintainsVariableSpace = true)>]
        member x.SupervisedBy(ctx:ActorConfiguration<'a,'b>, sup) = 
            { ctx with Parent = sup }
        [<CustomOperation("children", MaintainsVariableSpace = true)>]
        member x.Children(ctx:ActorConfiguration<'a,'b>, children) =
            { ctx with Children = children }
        [<CustomOperation("supervisorStrategy", MaintainsVariableSpace = true)>]
        member x.SupervisorStrategy(ctx:ActorConfiguration<'a,'b>, supervisorStrategy) = 
            { ctx with SupervisorStrategy = supervisorStrategy }
        [<CustomOperation("raiseEventsOn", MaintainsVariableSpace = true)>]
        member x.RaiseEventsOn(ctx:ActorConfiguration<'a,'b>, es) = 
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
    
    let context f = MH (fun ctx -> f ctx)

    let receive timeout = MH (fun ctx -> async {
        let! msg = ctx.Receive(?timeout = timeout)
        ctx.CurrentMessage <- msg
        ctx.SpanId <- Random.randomLong()
        Trace.Write (ctx.Path.ToString()) (Some msg.Id) (Some ctx.SpanId)
        return msg.Message  
    })

    let tryReceive timeout = MH (fun ctx -> async {
        let! msg = ctx.TryReceive(?timeout = timeout)
        return Option.map (fun msg -> 
            ctx.CurrentMessage <- msg; 
            ctx.SpanId <- Random.randomLong()
            Trace.Write (ctx.Path.ToString()) (Some msg.Id) (Some ctx.SpanId); 
            msg.Message) msg 
    })

    let scan timeout f = MH (fun ctx -> async {
        let! msg = ctx.Scan(f, ?timeout = timeout)
        ctx.CurrentMessage <- msg
        ctx.SpanId <- Random.randomLong()
        Trace.Write (ctx.Path.ToString()) (Some msg.Id) (Some ctx.SpanId)
        return msg.Message  
    })

    let tryScan timeout f = MH (fun ctx -> async {
        let! msg = ctx.TryScan(f, ?timeout = timeout)
        return Option.map (fun msg -> 
            ctx.CurrentMessage <- msg; 
            ctx.SpanId <- Random.randomLong()
            Trace.Write (ctx.Path.ToString()) (Some msg.Id) (Some ctx.SpanId); 
            msg.Message) msg
    })

    let internal deadLetter =
        lazy
            actor {
                name "deadLetter"
                body (
                    let rec loop() = messageHandler {
                        let! msg = receive None
                        return! loop()
                    }

                    loop()
                )
            } |> spawn

    let postDeadLetter (msg:Message<'a>) = deadLetter.Value.Post(Message.Box(msg)) 

    let internal tryGetSender() = 
        match CallContext.LogicalGetData("actor") with
        | :? ActorRef as a -> Some a
        | _ as a -> None //failwith "Unexpected type representing actorContext expected ActorRef got %A" a
    
    let postMessage (targets:ActorSelection) (msg:Message<obj>) =
        targets.Refs 
        |> List.iter (fun target ->
                        match target with
                        | ActorRef(target) -> target.Post(msg)
                        | Null -> postDeadLetter msg)

    let postWithSender (targets:ActorSelection) (sender:ActorRef) (msg:'a) = 
        postMessage targets (Message<'a>.Create(msg, sender))

    let rec post target (msg:'a) = 
        let sender = 
            match tryGetSender() with
            | Some a -> a
            | None -> deadLetter.Value
        postWithSender target sender msg

    let replyTo targets msg =
        MH (fun ctx -> async {
                do postMessage targets { Id = ctx.SpanId; Sender = ActorRef ctx.Self; Message = msg }
            }
        )

    let reply msg = MH (fun ctx -> async {
        do postMessage (ActorSelection([ctx.CurrentMessage.Sender])) { Id = ctx.SpanId; Sender = ActorRef ctx.Self; Message = msg }
    })

    let link actor (supervisor:ActorRef) = 
        post actor (SetParent(supervisor))

    let unlink target = 
        post target (SetParent(Null))

[<AutoOpen>]
module ActorOperators =
 
    let inline (!!) a = ActorSelection.op_Implicit a
    
    let inline (!~) a = lazy !!a  
    
    let inline (-->) msg t = 
        let a = ActorSelection.op_Implicit t
        Actor.post a msg
    
    let inline (<--) t msg =
        let a = ActorSelection.op_Implicit t
        Actor.post a msg