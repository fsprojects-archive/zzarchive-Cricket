namespace FSharp.Actor

module Metrics = 
    
    open System
    open System.Diagnostics
    open System.Collections.Concurrent
    open FSharp.Actor.Math.Statistics

    type MetricValue = 
        | Instantaneous of int64
        | Histogram of Sampling.Sample
        override x.ToString()  = 
            match x with
            | Instantaneous(v) -> v.ToString()
            | Histogram(s) -> sprintf "Min: %d, Max: %d, Average %.4f, StdDev: %.4f" s.Min s.Max s.Mean s.StandardDeviation
        member x.Value<'a>() = 
               match x with
               | Instantaneous(v) -> v |> box
               | Histogram(s) -> s |> box
               |> unbox<'a>
    
    type MetricValueStore = ConcurrentDictionary<string, MetricValue>

    type MetricContext = {
        Key : string
        Store : MetricValueStore
    }

    type MetricStore = ConcurrentDictionary<string, MetricContext>

    let private store = new MetricStore()

    let private addUpdate (ctx:MetricContext) (key:string) (value:MetricValue) = 
        ctx.Store.AddOrUpdate(key, value, (fun _ _ -> value)) |> ignore

    let private update<'a> (ctx:MetricContext) key updator = 
        let oldValue = ctx.Store.[key]
        let result, newValue = updator (oldValue.Value<'a>())
        ctx.Store.TryUpdate(key, newValue, oldValue) |> ignore
        result
    
    let createContext s =
        let ctx = { Key = s; Store = new MetricValueStore() }
        store.AddOrUpdate(ctx.Key, ctx, fun _ _ -> ctx) 

    let contextFromType (typ:Type) = createContext (typ.AssemblyQualifiedName)

    let createGuage ctx key = 
        addUpdate ctx key (Instantaneous 0L)
        (fun v -> 
            update<int64> ctx key (fun _ -> (), Instantaneous(v)) 
        )

    let createCounter ctx key = 
        addUpdate ctx key (Instantaneous 0L)
        (fun inc -> 
            update<int64> ctx key (fun v -> (), Instantaneous(v + inc))
        )

    let createTimer ctx key = 
        addUpdate ctx key (Histogram(Sampling.empty))
        let sw = Stopwatch() 
        (fun func -> 
                update<Sampling.Sample> ctx key (fun oldValue ->
                        sw.Reset(); sw.Start()
                        let result = func()
                        sw.Stop()
                        result, Histogram(Sampling.update oldValue sw.ElapsedMilliseconds)
                )
        )

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
        

    let createReport() =             
        getMetrics() 
        |> Seq.map (fun (k,v)  -> k, v |> Seq.map (fun (k,v) -> k, v.ToString()) |> Map.ofSeq)
        |> Map.ofSeq