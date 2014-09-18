namespace FSharp.Actor

open System
open System.IO
open System.Threading

type ActorHostConfiguration = {
     Transports : Map<string,ITransport>
     Registry : ActorRegistry
     EventStream : IEventStream
     Logger : Log.Logger
     Serializer : ISerializer
     Metrics : Metrics.Configuration option
     Name : string
}
with
    static member Create(?configuration : ActorHostConfiguration -> ActorHostConfiguration) =
        let hostName = Metrics.name
        let defaultConfig = 
            {
                Logger = Log.Logger(sprintf "ActorHost:[%s]" hostName, Log.defaultFor Log.Debug) 
                Transports = Map.empty; 
                Registry = new InMemoryActorRegistry()
                EventStream = new DefaultEventStream("host", Log.defaultFor Log.Debug)
                Serializer = new BinarySerializer()
                Metrics = Some Metrics.Configuration.Default
                Name = hostName
            }
        match configuration with
        | Some(cfg) -> cfg defaultConfig
        | None -> defaultConfig

     member x.AddTransports(transports) = 
        let addTransport transports (transport:ITransport) =
            Map.add transport.Scheme transport transports
        { x with Transports = Seq.fold addTransport x.Transports transports }

type ActorHost private (configuration:ActorHostConfiguration) as self = 
     
     static let mutable instance : ActorHost = Unchecked.defaultof<_>
     static let mutable isDisposed = false
     static let mutable isStarted = false

     let mutable configuration = configuration
     let metricContext = Metrics.createContext configuration.Name
     let resolutionRequests = Metrics.createCounter metricContext "actorResolutionRequests"
     let actorsRegisted = Metrics.createCounter metricContext "registeredActors"
     let uptime = Metrics.createUptime metricContext "uptime" 1000
     let cts = new CancellationTokenSource() 
           
     do
        isStarted <- true
        Map.iter (fun _ (t:ITransport) -> t.Start(configuration.Serializer, self.CancelToken)) configuration.Transports
        Option.iter (fun c -> Metrics.start(c, self.CancelToken)) configuration.Metrics
        configuration.Logger.Debug(sprintf "ActorHost started %s" self.Name)

     member internal x.Configure(f) = configuration <- (f configuration)
     member internal x.ResolveTransport transport = configuration.Transports.TryFind transport
     member internal x.Name with get() = configuration.Name
     member internal x.Serializer with get() = configuration.Serializer
     member internal x.EventStream with get() = configuration.EventStream
     member internal x.CancelToken with get() = cts.Token
     member internal x.Transports with get() = configuration.Transports |> Map.toSeq |> Seq.map snd
     member internal x.Actors with get() =  configuration.Registry.All

     member internal x.ResolveActor name = 
        resolutionRequests(1L)
        configuration.Registry.Resolve(name)

     member internal x.RegisterActor(ref:ActorRef) = 
        configuration.Registry.Register(ref)
        actorsRegisted(1L)

     member x.SubscribeEvents(eventF) = 
        configuration.EventStream.Subscribe(eventF)
        x
            
     static member Start(?configurator) = 
        let configurator = defaultArg configurator id
        instance <- new ActorHost(ActorHostConfiguration.Create configurator)
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
           configuration.Transports |> Map.toSeq |> Seq.iter (fun (_,v) -> v.Dispose())
           configuration.EventStream.Dispose()
           configuration.Logger.Info("shutdown")