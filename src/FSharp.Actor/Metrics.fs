namespace FSharp.Actor

module Metrics = 
    
    open System
    open System.IO
    open System.Diagnostics
    open System.Threading
    open System.Collections.Concurrent
    open FSharp.Actor.Math.Statistics
    
    let currentProcess = Diagnostics.Process.GetCurrentProcess()
    let processName, processId, machineName = currentProcess.ProcessName, currentProcess.Id, Environment.MachineName

    let name = sprintf "%s_%s_%d" machineName processName processId

    ///http://en.wikipedia.org/wiki/Moving_average#Application_to_measuring_computer_performance
    type MeterValues = {
        OneMinuteRate : ExponentialWeightedAverage.EWMA
        FiveMinuteRate : ExponentialWeightedAverage.EWMA
        FifteenMinuteRate : ExponentialWeightedAverage.EWMA
        StartTicks : int64
        Count : int64
    }
    with
        static member Empty = {
            OneMinuteRate = ExponentialWeightedAverage.create 5L 1.
            FiveMinuteRate = ExponentialWeightedAverage.create 5L 5.
            FifteenMinuteRate = ExponentialWeightedAverage.create 5L 15.
            StartTicks = DateTime.Now.Ticks
            Count = 0L
        }

        member x.Mean 
            with get() = 
               (float x.Count) / (float (TimeSpan(DateTime.Now.Ticks - x.StartTicks).Seconds)) 

        member x.Mark(v) =
            { x with
                OneMinuteRate = ExponentialWeightedAverage.mark x.OneMinuteRate v
                FiveMinuteRate = ExponentialWeightedAverage.mark x.FiveMinuteRate v
                FifteenMinuteRate = ExponentialWeightedAverage.mark x.FiveMinuteRate v
                Count = x.Count + v
            }

         member x.Tick() = 
            { x with
                OneMinuteRate = ExponentialWeightedAverage.tick x.OneMinuteRate
                FiveMinuteRate = ExponentialWeightedAverage.tick x.FiveMinuteRate
                FifteenMinuteRate = ExponentialWeightedAverage.tick x.FiveMinuteRate
            }

    type MeterMetric = {
        Mark : int64 -> unit
        Cancel : unit -> unit
    }

    type TimerMetric<'a> = (unit -> 'a) -> 'a
    type GuageMetric = (int64 -> unit)
    type CounterMetric = (int64 -> unit)

    type MetricValue = 
        | Instantaneous of int64
        | Delegated of (unit -> MetricValue)
        | Histogram of Sampling.Sample
        | Meter of MeterValues
        | Timespan of TimeSpan
        | Empty
        member x.AsArray() =
            match x with
            | Delegated(f) -> (f()).AsArray()
            | Instantaneous(v) -> 
                [| "Value", sprintf "%u" v |]
            | Histogram(s) -> 
                [|
                  "Min",  sprintf "%d" s.Min
                  "Max",  sprintf "%d" s.Max
                  "Average", sprintf "%.4f" s.Mean
                  "Standard Deviation", sprintf "%.4f" s.StandardDeviation
                |]
            | Timespan(s) -> [| "Time",  sprintf "%.4f" s.TotalMilliseconds |]
            | Meter(v) -> 
                [|
                    "One Minute Average", sprintf "%.4f" (v.OneMinuteRate.Value())
                    "Five Minute Average",  sprintf "%.4f" (v.FiveMinuteRate.Value())
                    "Fifteen Minute Average",   sprintf "%.4f" (v.FifteenMinuteRate.Value())
                    "Mean", sprintf "%.4f" v.Mean
                    "Count", sprintf "%d" v.Count
                |]
            | Empty -> [||]
        member x.Value<'a>() = 
               match x with
               | Delegated(f) -> (f()).Value<'a>() |> box
               | Instantaneous(v) -> v |> box
               | Histogram(s) -> s |> box
               | Meter(m) -> m |> box
               | Timespan(s) -> s |> box
               | Empty -> Unchecked.defaultof<'a> |> box 
               |> unbox<'a>

    
    type MetricValueStore = ConcurrentDictionary<string, MetricValue>

    type MetricContext = {
        Key : string
        Store : MetricValueStore
    }

    type Report = seq<string * seq<string * MetricValue>>

    type ReportCreator = 
        | HttpEndpoint of port:int
        | WriteToFile of interval:int * path:string * formatter:(Report -> string)
        | Custom of interval:int * (Report -> Async<unit>)
    
    type PerformanceCounterGroup = string * string
    type PerformanceCounterInstance = string * string

    module Formatters = 

        let toString (metrics:seq<string * seq<string * MetricValue>>) =
            let createMetricValue (name,v:MetricValue) =
                sprintf "%s\r\n\t\t%s" name (String.Join(Environment.NewLine + "\t\t", v.AsArray() |> Seq.map (fun (n,v) -> sprintf "%s = %A" n v)))

            let createMetricType (name, vs) = 
                 sprintf "%s\r\n\t%s" name (String.Join(Environment.NewLine + "\t", Seq.map createMetricValue vs |> Seq.toArray))

            String.Join(Environment.NewLine, metrics |> Seq.map createMetricType |> Seq.toArray)

        let toJsonString (metrics:seq<string * seq<string * MetricValue>>) = 
            let createMetricValue (name,v:MetricValue) =
                sprintf "{ \"Key\": %A,\"Properties\": [%s] }" name (String.Join(", ", v.AsArray() |> Seq.map (fun (n,v) -> sprintf "{ \"Name\": %A, \"Value\": %s }" n v)))

            let createMetricType (name, vs) = 
                 sprintf "{ \"Key\": %A, \"Values\": [%s] }" name (String.Join(", ", Seq.map createMetricValue vs |> Seq.toArray))

            "[" + String.Join(", " + Environment.NewLine, metrics |> Seq.map createMetricType |> Seq.toArray) + "]"

    type Configuration = {
        ReportCreator : ReportCreator
        CancellationToken : CancellationToken
        PerformanceCounters : (PerformanceCounterGroup * (PerformanceCounterInstance list)) list
    }
    with
        member x.AddPerformanceCounterGroup(grp:PerformanceCounterGroup * (PerformanceCounterInstance list)) = 
            { x with PerformanceCounters = grp :: x.PerformanceCounters}
        static member Default = 
            {
                ReportCreator = WriteToFile(10000, sprintf "metrics_%s_%s_%d.json" machineName processName processId, Formatters.toJsonString)
                CancellationToken = Async.DefaultCancellationToken
                PerformanceCounters = 
                    [
                        (".NET CLR LocksAndThreads", "locks_and_threads"), [
                           "Total # of Contentions", "number_of_contentions" 
                           "Contention Rate / Sec", "contention_rate"
                           "Current Queue Length", "current_queue_length"
                           "Queue Length Peak", "queue_length_peak"
                           "Queue Length / sec", "queue_length_per_sec"
                           "# of current logical Threads", "current_logical_threads"
                           "# of current physical Threads", "current_physical_threads"
                           "# of current recognized Threads", "current_recognized_threads"
                           "# of total recognized Threads", "current_total_recognized_threads"
                          ]
                        (".NET CLR Memory", "memory"), [
                            "# Bytes in all heaps", "bytes_in_all_heaps"
                            "Gen 0 heap size", "gen_0_heap_size"
                            "Gen 1 heap size", "gen_1_heap_size"
                            "Gen 2 heap size", "gen_2_heap_size"
                            "# Gen 0 Collections", "gen_0_collections"
                            "# Gen 1 Collections", "gen_1_collections"
                            "# Gen 2 Collections", "gen_2_collections"
                            "Large Object Heap size", "large_object_heap_size"
                            "% Time in GC", "percentage_time_in_GC"
                          ]
                          //These aren't instance based they are global
                        (".NET CLR Networking", "networking"), [
                            "Bytes Received", "bytes_received"
                            "Bytes Sent", "bytes_sent"
                            "Connections Established", "connections_established"
                            "Datagrams Received", "datagrams_received"
                            "Datagrams Sent", "datagrams_sent"
                          ]
                        (".NET CLR Networking 4.0.0.0", "networking"), [
                            "Bytes Received", "bytes_received"
                            "Bytes Sent", "bytes_sent"
                            "Connections Established", "connections_established"
                            "Datagrams Received", "datagrams_received"
                            "Datagrams Sent", "datagrams_sent"
                          ]
                   ]
            }

    type MetricStore = ConcurrentDictionary<string, MetricContext>

    let private store = new MetricStore()

    let mutable private isStarted = false

    let private ensureExists (ctx:MetricContext) (key:string) (value:MetricValue) = 
        if isStarted 
        then ctx.Store.GetOrAdd(key, value) |> ignore

    let private update<'a,'b> (ctx:MetricContext) key (updator : 'a -> 'b * MetricValue) =
        if isStarted
        then
            let oldValue = ctx.Store.[key]
            let result, newValue = updator (oldValue.Value<'a>())
            ctx.Store.TryUpdate(key, newValue, oldValue) |> ignore
            result
        else 
            updator (MetricValue.Empty.Value<'a>()) |> fst

    let private createReportSink getMetrics cancellationToken = function
        | Custom (interval, f) ->
            let rec reporter() = 
                async {
                   do! Async.Sleep(interval)
                   do! f(getMetrics()) 
                   return! reporter()        
                }
            Async.Start(reporter(), cancellationToken)
        | WriteToFile (interval, path, formatter) -> 
            let rec reporter() = 
                async {
                   do! Async.Sleep(interval)
                   do File.WriteAllText(path, sprintf "%s" (getMetrics() |> formatter)) 
                   return! reporter()        
                }
            Async.Start(reporter(), cancellationToken)  
        | HttpEndpoint(port) ->
            let httpListener = new Net.HttpListener()
            let rec listenerLoop() = async {
                let! ctx = httpListener.GetContextAsync() |> Async.AwaitTask
                let result = getMetrics()
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
                | Some("text/html") -> do! writeResponse "text/html" (Formatters.toString result)
                | Some("application/json") -> do! writeResponse "application/json" (Formatters.toJsonString result)
                | _ -> do! writeResponse "text/html" "Unsupported accept type"
                return! listenerLoop()    
            }
            httpListener.Prefixes.Add(sprintf "http://+:%d/" port)
            httpListener.Start()
            Async.Start(listenerLoop(), cancellationToken)
            cancellationToken.Register(fun () -> httpListener.Close()) |> ignore
    
    let createContext s =
        let ctx = { Key = s; Store = new MetricValueStore() }
        store.AddOrUpdate(ctx.Key, ctx, fun _ _ -> ctx) 

    let contextFromType (typ:Type) = createContext (typ.AssemblyQualifiedName)

    let createGuage ctx key : GuageMetric = 
        ensureExists ctx key (Instantaneous 0L)
        (fun v -> 
            update<int64, unit> ctx key (fun _ -> (), Instantaneous(v))
        )

    let createDelegatedGuage ctx key action = 
        ensureExists ctx key (Delegated(fun () -> Instantaneous(action())))  

    let createCounter ctx key : CounterMetric = 
        ensureExists ctx key (Instantaneous 0L)
        (fun inc -> 
            update<int64, unit> ctx key (fun v -> (), Instantaneous(v + inc))
        )

    let createTimer ctx key : TimerMetric<'a> = 
        ensureExists ctx key (Histogram(Sampling.empty))
        let sw = Stopwatch() 
        (fun func -> 
                update<Sampling.Sample, 'a> ctx key (fun oldValue ->
                        sw.Reset(); sw.Start()
                        let result = func()
                        sw.Stop()
                        result, Histogram(Sampling.update oldValue sw.ElapsedMilliseconds)
                )
        )

    let createUptime ctx key interval =
        ensureExists ctx key (Timespan TimeSpan.Zero)
        let cts = new CancellationTokenSource()
        let startTicks = DateTime.Now.Ticks
        let rec worker() = async {
            do! Async.Sleep(interval)
            do update<TimeSpan, unit> ctx key (fun _ -> (), Timespan(TimeSpan(DateTime.Now.Ticks - startTicks)))
            return! worker()
        }
        if isStarted then Async.Start(worker(), cts.Token)
        (fun () -> cts.Cancel())

    let createMeter ctx key : MeterMetric =
        ensureExists ctx key (Meter(MeterValues.Empty))
        let cts = new CancellationTokenSource()
        let rec worker() = async {
            do! Async.Sleep(5000)
            do update<MeterValues, unit> ctx key (fun oldValue -> (), Meter(oldValue.Tick()))
            return! worker()
        }
        if isStarted then Async.Start(worker(), cts.Token)
        { 
            Mark = (fun v -> update<MeterValues, unit> ctx key (fun oldValue -> (), Meter(oldValue.Mark(v))))
            Cancel = (fun () -> cts.Cancel())
        }

    let tryGetPerformanceCounter(category,counter,instance) = 
        try
            if PerformanceCounterCategory.CounterExists(category, counter)
            then
                let counter = new PerformanceCounter(category, counter, instance, true)
                Some(counter)
            else None
        with e ->
            None
    
    let createPerformanceCounterGuage ctx key perfCounter = 
        match tryGetPerformanceCounter perfCounter with
        | Some(counter) ->
            let reader() = 
                let value = counter.NextValue() |> int64
                Instantaneous(value)
            ensureExists ctx key (Delegated(reader))
        | None -> ()

    let addSystemMetrics(systemCounters) =
        let ctx = createContext "system"
        let instanceName = Path.GetFileNameWithoutExtension(processName)
        
        for ((category, metricCat), counters) in systemCounters do 
            for (counter, label) in counters do
                createPerformanceCounterGuage ctx (metricCat + "/" + label) (category, counter, instanceName)

    let getMetrics() = 
        seq { 
            for metric in store do
                let key = metric.Key
                let metrics = 
                    seq { 
                        for metric in metric.Value.Store do
                            yield metric.Key, metric.Value
                    }
                yield key, metrics
        }

    let start(config:Configuration, cancellationToken) =
        addSystemMetrics(config.PerformanceCounters)
        createReportSink getMetrics cancellationToken config.ReportCreator
        isStarted <- true