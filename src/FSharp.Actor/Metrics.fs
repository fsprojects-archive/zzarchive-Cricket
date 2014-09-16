namespace FSharp.Actor

module Metrics = 
    
    open System
    open System.IO
    open System.Diagnostics
    open System.Threading
    open System.Collections.Concurrent
    open FSharp.Actor.Math.Statistics
    
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

    type TimerMetric = (unit -> unit) -> unit
    type GuageMetric = (int64 -> unit)
    type CounterMetric = (int64 -> unit)

    type MetricValue = 
        | Instantaneous of int64
        | Delegated of (unit -> MetricValue)
        | Histogram of Sampling.Sample
        | Meter of MeterValues
        | Timespan of TimeSpan
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
        member x.Value<'a>() = 
               match x with
               | Delegated(f) -> (f()).Value<'a>() |> box
               | Instantaneous(v) -> v |> box
               | Histogram(s) -> s |> box
               | Meter(m) -> m |> box
               | Timespan(s) -> s |> box 
               |> unbox<'a>

    
    type MetricValueStore = ConcurrentDictionary<string, MetricValue>

    type MetricContext = {
        Key : string
        Store : MetricValueStore
    }

    type MetricStore = ConcurrentDictionary<string, MetricContext>

    let private store = new MetricStore()

    let private ensureExists (ctx:MetricContext) (key:string) (value:MetricValue) = 
        ctx.Store.GetOrAdd(key, value) |> ignore

    let private update<'a> (ctx:MetricContext) key updator = 
        let oldValue = ctx.Store.[key]
        let result, newValue = updator (oldValue.Value<'a>())
        ctx.Store.TryUpdate(key, newValue, oldValue) |> ignore
        result
    
        
    let currentProcess = Diagnostics.Process.GetCurrentProcess()
    let processName, processId, machineName = currentProcess.ProcessName, currentProcess.Id, Environment.MachineName

    let createContext s =
        let ctx = { Key = s; Store = new MetricValueStore() }
        store.AddOrUpdate(ctx.Key, ctx, fun _ _ -> ctx) 

    let contextFromType (typ:Type) = createContext (typ.AssemblyQualifiedName)

    let createGuage ctx key : GuageMetric = 
        ensureExists ctx key (Instantaneous 0L)
        (fun v -> 
            update<int64> ctx key (fun _ -> (), Instantaneous(v))
        )

    let createDelegatedGuage ctx key action = 
        ensureExists ctx key (Delegated(fun () -> Instantaneous(action())))  

    let createCounter ctx key : CounterMetric = 
        ensureExists ctx key (Instantaneous 0L)
        (fun inc -> 
            update<int64> ctx key (fun v -> (), Instantaneous(v + inc))
        )

    let createTimer ctx key : TimerMetric = 
        ensureExists ctx key (Histogram(Sampling.empty))
        let sw = Stopwatch() 
        (fun func -> 
                update<Sampling.Sample> ctx key (fun oldValue ->
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
            do update<TimeSpan> ctx key (fun _ -> (), Timespan(TimeSpan(DateTime.Now.Ticks - startTicks)))
            return! worker()
        }
        Async.Start(worker(), cts.Token)
        (fun () -> cts.Cancel())

    let createMeter ctx key : MeterMetric =
        ensureExists ctx key (Meter(MeterValues.Empty))
        let cts = new CancellationTokenSource()
        let rec worker() = async {
            do! Async.Sleep(5000)
            do update<MeterValues> ctx key (fun oldValue -> (), Meter(oldValue.Tick()))
            return! worker()
        }
        Async.Start(worker(), cts.Token)
        { 
            Mark = (fun v -> update<MeterValues> ctx key (fun oldValue -> (), Meter(oldValue.Mark(v))))
            Cancel = (fun () -> cts.Cancel())
        }

    let getPerformanceCounter(category,counter,instance) = 
        new PerformanceCounter(category, counter, instance, true)
    
    let createPerformanceCounterGuage ctx key perfCounter = 
        let perfCounter = getPerformanceCounter perfCounter
        ensureExists ctx key (Delegated(fun () -> 
                let value = perfCounter.NextValue() |> int64
                printfn "Reading perf counter %A %d" perfCounter.CounterName value
                Instantaneous(value)))

    let addSystemMetrics() =
        let ctx = createContext "system_metrics"
        let instanceName = Path.GetFileNameWithoutExtension(processName)
        let systemCounters = [
            (".NET CLR LocksAndThreads", "locks_and_threads"), [
               "Total # of Contentions", "number_of_contentions" 
               "Contention Rate / Sec", "contention_rate"
               "Current Queue Length", "current_queue_length"
              ]
            (".NET CLR Memory", "memory"), [
                "# Bytes in all heaps", "bytes_in_all_heaps"
                "Gen 0 heap size" , "gen_0_heap_size" 
              ]
            ]
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