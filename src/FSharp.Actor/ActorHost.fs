namespace FSharp.Actor

open System
open System.IO
open System.Threading
open FSharp.Actor.Diagnostics

type ActorHostConfiguration = {
     Registry : ActorRegistry
     EventStream : IEventStream
     Logger : Log.Logger
     Serializer : ISerializer
     CancellationToken : CancellationToken
     Name : string
}
with
    static member Create(?name, ?logger, ?serializer, ?registry, ?eventStream, ?cancellationToken) =
        let hostName = defaultArg name Environment.DefaultActorHostName
        let serializer = defaultArg serializer (new BinarySerializer() :> ISerializer)
        let ct = defaultArg cancellationToken Async.DefaultCancellationToken
        {
            Logger = defaultArg logger (Log.Logger(sprintf "ActorHost:[%s]" hostName, Log.defaultFor Log.Debug))
            Registry = defaultArg registry (new InMemoryActorRegistry() :> ActorRegistry)
            EventStream = defaultArg eventStream (new DefaultEventStream("host", Log.defaultFor Log.Debug) :> IEventStream)
            Serializer = serializer
            CancellationToken = ct
            Name = hostName
        }


type ActorHost private (configuration:ActorHostConfiguration) = 
     
     static let mutable instance : ActorHost = Unchecked.defaultof<_>
     static let mutable isDisposed = false
     static let mutable isStarted = false
     static let cts = new CancellationTokenSource() 

     let mutable configuration = configuration

     do
        isStarted <- true
        configuration.Logger.Debug(sprintf "ActorHost started %s" configuration.Name)

     let metricContext = Metrics.createContext configuration.Name
     let resolutionRequests = Metrics.createCounter(metricContext,"actorResolutionRequests")
     let actorsRegisted = Metrics.createCounter(metricContext,"registeredActors")
     let uptime = Metrics.createUptime(metricContext,"uptime", 1000)

     member internal x.Configure(f) = configuration <- (f configuration)
     
     member internal x.Name with get() = configuration.Name
     member internal x.Serializer with get() = configuration.Serializer
     member internal x.EventStream with get() = configuration.EventStream
     member internal x.CancelToken with get() = cts.Token
     
     member internal x.ResolveActor name = 
        resolutionRequests(1L)
        configuration.Registry.Resolve(name)

     member internal x.RegisterActor(ref:ActorRef) = 
        configuration.Registry.Register(ref)
        actorsRegisted(1L)

     member x.SubscribeEvents(eventF) = 
        configuration.EventStream.Subscribe(eventF)
        x
  
     static member Start(?name, ?logger, ?serializer, ?registry, ?metrics, ?tracing, ?cancellationToken) = 
        let config = (ActorHostConfiguration.Create(?name = name, ?logger = logger, 
                                                    ?serializer = serializer, ?registry = registry, 
                                                    ?cancellationToken = cancellationToken))
        instance <- new ActorHost(config)
        Metrics.start(metrics)
        Trace.start(tracing)
        instance      
     
     static member Dispose() = 
        (instance :> IDisposable).Dispose()

     static member Instance with get() = instance
     
     interface IDisposable with
        member x.Dispose() =
           isDisposed <- true
           configuration.Logger.Info("shutting down")
           cts.Cancel()
           cts.Dispose()
           configuration.Registry.Dispose()
           configuration.EventStream.Dispose()
           Metrics.dispose()
           Trace.dispose()
           configuration.Logger.Info("shutdown")
