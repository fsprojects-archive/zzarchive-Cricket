namespace Cricket

open System
open System.Threading
open Cricket.Diagnostics

type ActorHostConfiguration = {
     Registry : ActorRegistry
     EventStream : IEventStream
     LogWriters : seq<ILogWriter>
     CancellationToken : CancellationToken
     Name : string
}
with
    static member Create(?name, ?loggers, ?registry, ?eventStream, ?cancellationToken) =
        let hostName = defaultArg name Environment.DefaultActorHostName
        let ct = defaultArg cancellationToken Async.DefaultCancellationToken

        let getDefaultLogWriters() = 
            [
              ConsoleWindowLogWriter(LogLevel.Debug) :> ILogWriter
              OutputWindowLogWriter(LogLevel.Debug)  :> ILogWriter
            ]

        let logWriters = (defaultArg loggers (getDefaultLogWriters()) |> Seq.toList)
        Logger.setLogWriters logWriters
        {
            LogWriters = logWriters
            Registry = defaultArg registry (new InMemoryActorRegistry() :> ActorRegistry)
            EventStream = defaultArg eventStream (new DefaultEventStream("host") :> IEventStream)
            CancellationToken = ct
            Name = hostName
        }


type ActorHost private (configuration:ActorHostConfiguration) = 
     
     let logger = Logger.create configuration.Name
     static let mutable instance : ActorHost = Unchecked.defaultof<_>
     static let mutable isDisposed = false
     static let mutable isStarted = false
     static let cts = new CancellationTokenSource() 

     let mutable configuration = configuration

     do
        isStarted <- true
        logger.Debug(sprintf "ActorHost started %s" configuration.Name)

     let metricContext = Metrics.createContext configuration.Name
     let resolutionRequests = Metrics.createCounter(metricContext,"actorResolutionRequests")
     let actorsRegisted = Metrics.createCounter(metricContext,"registeredActors")
     let uptime = Metrics.createUptime(metricContext,"uptime", 1000)

     member internal __.Configure(f) = configuration <- (f configuration)
     
     member internal __.Name with get() = configuration.Name
     member internal __.EventStream with get() = configuration.EventStream
     member internal __.CancelToken with get() = cts.Token
     
     member internal __.ResolveActor name = 
        resolutionRequests(1L)
        configuration.Registry.Resolve(name)

     member internal __.RegisterActor(ref:ActorRef) = 
        configuration.Registry.Register(ref)
        actorsRegisted(1L)

     member x.SubscribeEvents(eventF) = 
        configuration.EventStream.Subscribe(eventF)
        x
  
     static member Start(?name, ?loggers, ?registry, ?metrics, ?tracing, ?cancellationToken) = 
        let config = (ActorHostConfiguration.Create(?name = name,
                                                    ?loggers = loggers, 
                                                    ?registry = registry, 
                                                    ?cancellationToken = cancellationToken))
        instance <- new ActorHost(config)
        Metrics.start(metrics)
        Trace.start(tracing)
        instance      
     
     static member Dispose() = 
        (instance :> IDisposable).Dispose()

     static member Instance with get() = instance
     
     interface IDisposable with
        member __.Dispose() =
           isDisposed <- true
           logger.Info("shutting down")
           cts.Cancel()
           cts.Dispose()
           configuration.Registry.Dispose()
           configuration.EventStream.Dispose()
           Metrics.dispose()
           Trace.dispose()
           logger.Info("shutdown")
