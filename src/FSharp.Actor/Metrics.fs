namespace FSharp.Actor

module Metrics = 
    
    open System
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
        | Histogram of Sampling.Sample
        | Meter of MeterValues
        | Timespan of TimeSpan
        member x.AsStringArray() =
            match x with
            | Instantaneous(v) -> 
                [| sprintf "Value = %d" v |]
            | Histogram(s) -> 
                [|
                  sprintf "Min = %d" s.Min
                  sprintf "Max = %d" s.Max
                  sprintf "Average %.4f" s.Mean
                  sprintf "Standard Deviation = %.4f" s.StandardDeviation
                |]
            | Timespan(s) -> [| sprintf "Time = %s" (s.ToString()) |]
            | Meter(v) -> 
                [|
                    sprintf "Average One Minute = %.4f values per second" (v.OneMinuteRate.Value())
                    sprintf "Average Five Minute = %.4f values per second" (v.FiveMinuteRate.Value())
                    sprintf "Average Fifteen Minute = %.4f values per second" (v.FifteenMinuteRate.Value())
                    sprintf "Mean = %.4f values per second" v.Mean
                    sprintf "Count = %d"  v.Count
                |]
        member x.Value<'a>() = 
               match x with
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
    
    let createContext s =
        let ctx = { Key = s; Store = new MetricValueStore() }
        store.AddOrUpdate(ctx.Key, ctx, fun _ _ -> ctx) 

    let contextFromType (typ:Type) = createContext (typ.AssemblyQualifiedName)

    let createGuage ctx key : GuageMetric = 
        ensureExists ctx key (Instantaneous 0L)
        (fun v -> 
            update<int64> ctx key (fun _ -> (), Instantaneous(v)) 
        )

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

    let addSystemMetrics ctx =
        ()

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
            String.Join(Environment.NewLine, 
                        metrics
                        |> Seq.map (fun (n, vs) ->
                            let createLines (name,v:MetricValue) =
                                sprintf "%s\r\n\t\t%s" name (String.Join(Environment.NewLine + "\t\t", v.AsStringArray()))
                            sprintf "%s\r\n\t%s" n (String.Join(Environment.NewLine + "\t", Seq.map createLines vs |> Seq.toArray))
                        ) |> Seq.toArray)