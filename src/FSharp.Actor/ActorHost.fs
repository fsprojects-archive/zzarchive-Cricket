namespace FSharp.Actor

open System
open System.Collections.Concurrent

type ActorSystemConfiguration = {
     mutable Registry : ActorRegistry
     mutable EventStream : IEventStream
     mutable Logger : Log.Logger
     mutable OnError : (ErrorContext -> unit)
     mutable CancellationToken : Threading.CancellationToken
}

type ActorSystem internal(systemName, serializer:ISerializer, ?configurator) as self = 
    
    let mutable supervisor = Unchecked.defaultof<_>
    let mutable deadLetter = Unchecked.defaultof<_>

    let configuration =
        {
            Logger = Log.Logger(sprintf "ActorSystem:[%s]" systemName, Log.defaultFor Log.Debug) 
            Registry = new InMemoryActorRegistry()
            EventStream = new DefaultEventStream(Log.defaultFor Log.Debug)
            CancellationToken = Async.DefaultCancellationToken
            OnError = (fun ctx -> ctx.Sender <-- Restart)
        }

    let createSupervisor() = 
        actor {
                 path (ActorPath.supervisor (systemName.TrimStart('/')))
                 supervisorStrategy configuration.OnError
              }
        |> self.SpawnActor

    let createDeadLetter() = 
        actor {
            path ActorPath.deadLetter
        } 
        |> self.SpawnActor

    do 
        (defaultArg configurator ignore) configuration
    
    member x.Name with get() = systemName
    member x.EventStream with get() = configuration.EventStream
    member x.CancelToken with get() = configuration.CancellationToken
    member x.Serializer with get() = serializer

    member internal x.Configure(configurator : ActorSystemConfiguration -> unit) = configurator configuration

    member x.SpawnActor (config : ActorConfiguration<'a>) = 
        let config = { config with Path = ActorPath.setSystem systemName config.Path; EventStream = Some(defaultArg config.EventStream (configuration.EventStream)) } 
        let actor = ActorRef(new Actor<_>(config) :> IActor)
        actor <-- SetParent(supervisor)
        configuration.Registry.Register actor
        actor

    member x.ResolveActor path = 
        configuration.Registry.Resolve path

    member x.SubscribeEvents(eventF) = 
        configuration.EventStream.Subscribe(eventF)
        x

    member x.Start() = 
        supervisor <- createSupervisor()
        deadLetter <- createDeadLetter()

        configuration.Logger.Debug(sprintf "ActorSystem started %s" x.Name)
        x

type ActorSystemRegistry = IRegistry<string, ActorSystem>

type ActorHostConfiguration = {
     mutable Transports : Map<string,ITransport>
     mutable Registry : ActorSystemRegistry
     mutable EventStream : IEventStream
     mutable Logger : Log.Logger
     mutable Serializer : ISerializer
     mutable CancellationToken : Threading.CancellationToken
}

type ActorHost() = 
     
     static let currentProcess = Diagnostics.Process.GetCurrentProcess()
     static let hostName = sprintf "%s:%d@%s" currentProcess.ProcessName currentProcess.Id currentProcess.MachineName
     static let mutable isStarted = false

     static let configuration = 
        {
            Logger = Log.Logger(sprintf "ActorHost:[%s]" hostName, Log.defaultFor Log.Debug) 
            Transports = Map.empty; 
            Registry = new ConcurrentDictionaryBasedRegistry<string,ActorSystem>(fun sys -> sys.Name)
            EventStream = new DefaultEventStream(Log.defaultFor Log.Debug)
            CancellationToken = Async.DefaultCancellationToken
            Serializer = new BinarySerializer()
        }

     static let onlyIfStarted f = 
        if isStarted
        then f()
        else failwithf "ActorHost not started, please call ActorHost.Start, this is usually the first thing to do in a Actor Based application"
     
     static member Configure(configurator : ActorHostConfiguration -> unit) = configurator configuration

     static member private AddTransports(transports) = 
            let addTransport transports (transport:ITransport) =
                Map.add transport.Scheme transport transports

            ActorHost.Configure (fun c -> 
                c.Transports <- Seq.fold addTransport c.Transports transports
            )

     static member ResolveTransport transport = 
        onlyIfStarted(fun () -> configuration.Transports.TryFind transport)

     static member CreateSystem(name, ?configurator) =
        onlyIfStarted(fun () -> 
            if not <| configuration.Registry.Resolve(name).IsEmpty
            then 
                let msg = sprintf "Failed to create system, a system named %s is already registered" name
                configuration.Logger.Error(msg)
                failwith msg
            else 
                let sys = (new ActorSystem(name, configuration.Serializer, ?configurator = configurator)).Start()
                configuration.Registry.Register(sys)
                sys)

     static member Systems with get() = onlyIfStarted(fun () -> configuration.Registry.All)

     static member TryResolveSystem name = 
        onlyIfStarted(fun () -> 
            match configuration.Registry.Resolve(name) with
            | sys :: _ -> Some sys
            | [] -> None)

     static member Start(?transports) =
        if not isStarted
        then
            ActorHost.AddTransports(defaultArg transports []) 
            Map.iter (fun _ (t:ITransport) -> t.Start(configuration.Serializer, configuration.CancellationToken)) configuration.Transports
            isStarted <- true
            configuration.Logger.Debug(sprintf "ActorHost started %s" hostName)

module Actor = 
    
    let link (actor:actorRef) (supervisor:actorRef) = 
        actor <-- SetParent(supervisor)

    let unlink (actor:actorRef) = 
        actor <-- SetParent(Null);

    let spawn systemName (config:ActorConfiguration<'a>) =
        match ActorHost.TryResolveSystem systemName with
        | Some(sys) -> sys.SpawnActor config
        | None -> failwithf "Cannot spawn actor no system %s registered" systemName