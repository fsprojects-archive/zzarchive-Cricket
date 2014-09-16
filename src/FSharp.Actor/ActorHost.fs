namespace FSharp.Actor

open System
open System.IO
open System.Threading
open Microsoft.FSharp.Control.CommonExtensions
open System.Collections.Concurrent

type ActorSystemConfiguration = {
     mutable Registry : ActorRegistry
     mutable EventStream : IEventStream
     mutable Logger : Log.Logger
     mutable OnError : (ErrorContext -> unit)
     mutable CancellationToken : CancellationToken
}

type ActorSystem internal(systemName, serializer:ISerializer, ?configurator) as self = 
     
    let mutable supervisor = Unchecked.defaultof<_>

    let metricContext = Metrics.createContext systemName
    let actorCount = Metrics.createCounter metricContext "actorCount"
    let uptimeCancel = Metrics.createUptime metricContext "uptime" 1000

    let configuration =
        {
            Logger = Log.Logger(sprintf "ActorSystem/%s" systemName, Log.defaultFor Log.Debug) 
            Registry = new InMemoryActorRegistry()
            EventStream = new DefaultEventStream(systemName, Log.defaultFor Log.Debug)
            CancellationToken = Async.DefaultCancellationToken
            OnError = (fun ctx -> ctx.Sender <-- Restart)
        }

    let createSupervisor() = 
        actor {
                 path (ActorPath.supervisor (systemName.TrimStart('/')))
                 supervisorStrategy configuration.OnError
              }
        |> self.SpawnActor

    let dispose() = 
        configuration.Logger.Info("shutting down")
        supervisor <-- Shutdown
        
        configuration.Registry.Dispose()
        configuration.EventStream.Dispose()
        configuration.Logger.Info("shutdown")
        uptimeCancel()

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
        actorCount(1L)
        actor

    member x.ResolveActor path = 
        configuration.Registry.Resolve path

    member x.SubscribeEvents(eventF) = 
        configuration.EventStream.Subscribe(eventF)
        x

    member x.Start() = 
        supervisor <- createSupervisor()
        
        configuration.Logger.Debug(sprintf "ActorSystem started %s" x.Name)
        x

    interface IDisposable with
        member x.Dispose() = dispose()

type ActorSystemRegistry = IRegistry<string, ActorSystem>

type MetricsReportHandler = 
    | HttpEndpoint of port:int
    | WriteToFile of path:string
    | Custom of (seq<string * seq<string * Metrics.MetricValue>> -> Async<unit>)

type MetricsConfiguration = {
    ReportInterval : int
    Handler : MetricsReportHandler
}

type ActorHostConfiguration = {
     mutable Transports : Map<string,ITransport>
     mutable Registry : ActorSystemRegistry
     mutable EventStream : IEventStream
     mutable Logger : Log.Logger
     mutable Serializer : ISerializer
     mutable CancellationToken : CancellationToken
     mutable Metrics : MetricsConfiguration option
     mutable Name : string
}

type DeadLetterMessage = DeadLetterMessage of Message<obj>

type ActorHost() = 
    
     static let hostName = sprintf "%s:%d@%s" Metrics.processName Metrics.processId Metrics.machineName
     static let metricContext = Metrics.createContext hostName
     static let systemCount = Metrics.createCounter metricContext "systemCount"
     static let mutable isStarted = false
     static let mutable isDisposed = false
     static let cts = new CancellationTokenSource()
     static let mutable system : ActorSystem = Unchecked.defaultof<_> 
       

     static let configuration =
        {
            Logger = Log.Logger(sprintf "ActorHost:[%s]" hostName, Log.defaultFor Log.Debug) 
            Transports = Map.empty; 
            Registry = new ConcurrentDictionaryBasedRegistry<string,ActorSystem>(fun sys -> sys.Name)
            EventStream = new DefaultEventStream("ActorHost/eventstream", Log.defaultFor Log.Debug)
            CancellationToken = cts.Token
            Serializer = new BinarySerializer()
            Metrics = None
            Name = hostName
        }

     static let onlyIfStarted f = 
        if (not isDisposed)
        then
            if isStarted
            then f()
            else failwithf "ActorHost not started, please call ActorHost.Start, this is usually the first thing to do in a Actor Based application"
        else raise(ObjectDisposedException("ActorHost"))


     static let mutable deadLetter : actorRef = actorRef.Null

     static let createDeadLetter() = 
            actor {
                path (ActorPath.deadLetter "deadletter")
                messageHandler (fun ctx -> 
                    let rec loop() = async {
                        let! msg = ctx.Receive()
                        configuration.EventStream.Publish(DeadLetterMessage(msg))
                        return! loop()
                    }

                    loop()
                )
            } |> system.SpawnActor
               
     static member Configure(configurator : ActorHostConfiguration -> unit) = configurator configuration

     static member PostDeadLetterMessage(msg) = 
            post deadLetter msg

     static member private AddTransports(transports) = 
            let addTransport transports (transport:ITransport) =
                Map.add transport.Scheme transport transports

            ActorHost.Configure (fun c -> 
                c.Transports <- Seq.fold addTransport c.Transports transports
            )

     static member private ReportMetrics(interval, handler) = 
        Metrics.addSystemMetrics()
        match handler with
        | Custom f ->
            let rec reporter() = 
                async {
                   do! Async.Sleep(interval)
                   do! f(Metrics.getMetrics()) 
                   return! reporter()        
                }
            Async.Start(reporter(), configuration.CancellationToken)
        | WriteToFile path -> 
            let rec reporter() = 
                async {
                   do! Async.Sleep(interval)
                   do File.WriteAllText(path, sprintf "%s" (Metrics.getMetrics() |> Metrics.Formatters.toString)) 
                   return! reporter()        
                }
            Async.Start(reporter(), configuration.CancellationToken)  
        | HttpEndpoint(port) ->
            let httpListener = new Net.HttpListener()
            let rec listenerLoop() = async {
                let! ctx = httpListener.GetContextAsync() |> Async.AwaitTask
                let result = Metrics.getMetrics()
                let writeResponse contentType (responseString:string) = async {
                        let bytes = Text.Encoding.UTF8.GetBytes(responseString)
                        ctx.Response.ContentType <- contentType
                        ctx.Response.ContentEncoding <- Text.Encoding.UTF8
                        ctx.Response.ContentLength64 <- bytes.LongLength
                        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
                        ctx.Response.OutputStream.Flush()
                        ctx.Response.OutputStream.Close()
                    }
                let contentType =
                    [|
                        "application/json"
                        "text/html"
                    |] |> Array.tryFind (fun x -> ctx.Request.AcceptTypes |> Array.exists (fun a -> a = x))
                
                match contentType with
                | Some("text/html") -> do! writeResponse "text/html" (Metrics.Formatters.toString result)
                | Some("application/json") -> do! writeResponse "application/json" (Metrics.Formatters.toJsonString result)
                | _ -> do! writeResponse "text/html" "Unsupported accept type"
                return! listenerLoop()    
            }
            httpListener.Prefixes.Add(sprintf "http://+:%d/" port)
            httpListener.Start()
            Async.Start(listenerLoop(), configuration.CancellationToken)
            configuration.CancellationToken.Register(fun () -> httpListener.Close()) |> ignore
                     

     static member ResolveTransport transport = 
        onlyIfStarted(fun () -> configuration.Transports.TryFind transport)

     static member Transports with get() = configuration.Transports |> Map.toSeq |> Seq.map snd

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
                systemCount(1L)
                sys)

     static member Systems with get() = onlyIfStarted(fun () -> configuration.Registry.All)

     static member TryResolveSystem name = 
        onlyIfStarted(fun () -> 
            match configuration.Registry.Resolve(name) with
            | sys :: _ -> Some sys
            | [] -> None)
             
     static member Start(?transports) =        
        if (not isDisposed)
        then
            if not isStarted
            then
                configuration.CancellationToken.Register(fun () -> ActorHost.Dispose()) |> ignore
                ActorHost.AddTransports(defaultArg transports []) 
                Map.iter (fun _ (t:ITransport) -> t.Start(configuration.Serializer, configuration.CancellationToken)) configuration.Transports
                isStarted <- true
                Option.iter (fun c -> ActorHost.ReportMetrics(c.ReportInterval, c.Handler)) configuration.Metrics
                system <- ActorHost.CreateSystem("global", (fun c -> c.EventStream <- configuration.EventStream; 
                                                                     c.CancellationToken <- configuration.CancellationToken)
                                                            )
                deadLetter <- createDeadLetter()
                configuration.Logger.Debug(sprintf "ActorHost started %s" hostName)
        else raise(ObjectDisposedException("ActorHost"))

     static member Dispose() = 
        configuration.Logger.Info("shutting down")
        cts.Cancel()
        cts.Dispose()
        configuration.Registry.Dispose()
        configuration.Transports |> Map.toSeq |> Seq.iter (fun (_,v) -> v.Dispose())
        configuration.EventStream.Dispose()
        configuration.Logger.Info("shutdown")

module Actor = 
    
    let link (actor:actorRef) (supervisor:actorRef) = 
        actor <-- SetParent(supervisor)

    let unlink (actor:actorRef) = 
        actor <-- SetParent(Null);

    let spawn systemName (config:ActorConfiguration<'a>) =
        match ActorHost.TryResolveSystem systemName with
        | Some(sys) -> sys.SpawnActor config
        | None -> failwithf "Cannot spawn actor no system %s registered" systemName