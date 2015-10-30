#nowarn "686"
namespace Cricket.Diagnostics
    
open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Collections.Concurrent
open Cricket
open Cricket.Math.Statistics

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


type MeterMetric = (int64 -> unit)
type TimerMetric<'a> = (unit -> 'a) -> 'a
type GuageMetric = (int64 -> unit)
type CounterMetric = (int64 -> unit)
type UptimeMetric = {
    Reset : (unit -> unit)
    Stop : (unit -> unit)
    Start : (unit -> unit)
}

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

type PerformanceCounterGroup = string * string
type PerformanceCounterInstance = string * string
type Report = seq<string * seq<string * MetricValue>>
type MetricStore = ConcurrentDictionary<string, MetricContext>

type MetricsConfiguration = {
    IsEnabled : bool
    CancellationToken : CancellationToken
    SystemMetrics : bool * (PerformanceCounterGroup * (PerformanceCounterInstance list)) list
}
with
    static member Default = 
        {
            IsEnabled = true
            CancellationToken = Async.DefaultCancellationToken
            SystemMetrics = 
                true, [
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

module Metrics =
    
    let private disposables = new ResizeArray<IDisposable>()
    let mutable private config = MetricsConfiguration.Default 
    let mutable private store = new MetricStore()

    let private ensureExists (ctx:MetricContext) (key:string) (value:MetricValue) = 
        ctx.Store.GetOrAdd(key, value) |> ignore

    let private update (ctx:MetricContext) key (updator : 'a -> 'b * MetricValue) =
        let oldValue = ctx.Store.[key]
        let result, newValue = updator (oldValue.Value<'a>())
        ctx.Store.TryUpdate(key, newValue, oldValue) |> ignore
        result
    
    let private addDisposer f = 
        let disposer = 
            { new IDisposable with
                member x.Dispose() = f()
            }
        disposables.Add(disposer)
        disposer

    let private tryGetPerformanceCounter(category,counter,instance) = 
        try
            if PerformanceCounterCategory.CounterExists(category, counter)
            then
                let counter = new PerformanceCounter(category, counter, instance, true)
                Some(counter)
            else None
        with e ->
            None

    let createContext(s) =
        let ctx = { Key = s; Store = new MetricValueStore() }
        store.AddOrUpdate(ctx.Key, ctx, fun _ _ -> ctx) 

    let createGuage(ctx,key) : GuageMetric =
        if config.IsEnabled
        then 
            ensureExists ctx key (Instantaneous 0L)
            (fun v -> 
                update<int64, unit> ctx key (fun _ -> (), Instantaneous(v))
            )
        else (fun _ -> ())

    let createDelegatedGuage(ctx,key,action) = 
        ensureExists ctx key (Delegated(fun () -> Instantaneous(if config.IsEnabled then action() else 0L)))  

    let createCounter(ctx,key) : CounterMetric = 
        ensureExists ctx key (Instantaneous 0L)
        (fun inc ->
            if config.IsEnabled
            then update<int64, unit> ctx key (fun v -> (), Instantaneous(v + inc))
            else ()
        )

    let createTimer(ctx,key) : TimerMetric<'a> = 
        ensureExists ctx key (Histogram(Sampling.empty))
        let sw = Stopwatch() 
        (fun func ->
                if config.IsEnabled
                then 
                    update<Sampling.Sample, 'a> ctx key (fun oldValue ->
                            sw.Reset(); sw.Start()
                            let result = func()
                            sw.Stop()
                            result, Histogram(Sampling.update oldValue sw.ElapsedMilliseconds)
                    )
                else func()
        )

    let createUptime(ctx,key,interval) =
        if config.IsEnabled
        then
            let stopWatch = new Stopwatch()
            ensureExists ctx key (Delegated(fun () -> Timespan(stopWatch.Elapsed)))
            {
                Reset = (fun () -> stopWatch.Reset())
                Stop = (fun () -> stopWatch.Stop())
                Start = (fun () -> stopWatch.Start())
            }
        else 
            {
                Reset = (fun () -> ())
                Stop = (fun () -> ())
                Start = (fun () -> ())
            }

    let createMeter(ctx,key) : MeterMetric =
        if config.IsEnabled
        then
            ensureExists ctx key (Meter(MeterValues.Empty))
            let cts = new CancellationTokenSource()
            let rec worker() = async {
                do! Async.Sleep(5000)
                do update<MeterValues, unit> ctx key (fun oldValue -> (), Meter(oldValue.Tick()))
                return! worker()
            }
            Async.Start(worker(), cts.Token)
            addDisposer (fun () -> cts.Cancel()) |> ignore
            (fun v -> update<MeterValues, unit> ctx key (fun oldValue -> (), Meter(oldValue.Mark(v))))
        else (fun _ -> ())
    
    let createPerformanceCounterGuage(ctx,key,perfCounter) =
        if config.IsEnabled 
        then
            match tryGetPerformanceCounter perfCounter with
            | Some(counter) ->
                let reader() = 
                    let value = counter.NextValue() |> int64
                    Instantaneous(value)
                ensureExists ctx key (Delegated(reader))
                addDisposer (fun () -> counter.Dispose()) |> ignore
            | None -> ()

    let configureSystemMetrics(enabled, systemCounters) =
        // if config.IsEnabled && enabled
        // then
        //     let ctx = createContext("system")
        //     let instanceName = Path.GetFileNameWithoutExtension(Environment.ProcessName)
            
        //     for ((category, metricCat), counters) in systemCounters do 
        //         for (counter, label) in counters do
        //             createPerformanceCounterGuage(ctx,(metricCat + "/" + label),(category, counter, instanceName))
        ()

    let dispose() = 
        disposables |> Seq.iter (fun d -> d.Dispose())
        disposables.Clear()
        store.Values |> Seq.iter (fun v -> v.Store.Clear())
        store.Clear()

    let start(cfg:MetricsConfiguration option) =
        Option.iter (fun cfg -> config <- cfg) cfg
        config.CancellationToken.Register(fun () -> dispose()) |> ignore
        configureSystemMetrics(config.SystemMetrics)


    let getReport() =
           seq {
               if config.IsEnabled
               then
                   for metric in store do
                       let key = metric.Key
                       let metrics = 
                           seq { 
                               for metric in metric.Value.Store do
                                   yield metric.Key, metric.Value
                           }
                       yield key, metrics
           }
