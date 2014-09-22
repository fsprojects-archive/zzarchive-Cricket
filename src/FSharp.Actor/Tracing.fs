namespace FSharp.Actor.Diagnostics

open System
open FSharp.Actor
open System.Diagnostics.Tracing

type TraceHeader =
    { Annotation : string[]
      Timestamp : int64 //For remoting this is not good enough need a vector or matrix clock. 
      SpanId : uint64
      ParentId : uint64 option }
    static member Empty =
        { Annotation = [||]; Timestamp = 0L; SpanId = 0UL; ParentId = None }
    static member Create(annotation, ?parentId, ?spanId) =
        let new_id = Random.randomLong()
        { Annotation = annotation; Timestamp = DateTime.UtcNow.Ticks; SpanId = defaultArg spanId new_id; ParentId = parentId }

type ITraceSink =
    inherit IDisposable
    abstract WriteTrace : string * uint64 option * uint64 option -> unit
    abstract WriteTrace : string  * string * uint64 option * uint64 option -> unit

type InMemoryTraceSink() =
     let store = new ResizeArray<TraceHeader>()

     member x.GetTraces() = store |> Seq.map id

     interface ITraceSink with 
        member x.WriteTrace(annotation,parent,span) = (x :> ITraceSink).WriteTrace(annotation, "", parent, span)
        member x.WriteTrace(annotation, eventType,parent,span) = 
            //TODO: Implement this properly.. with System.Diagnostics.Tracing. 
            store.Add (TraceHeader.Create([|annotation; eventType|], ?parentId = parent, ?spanId = span))
        member x.Dispose() = store.Clear()
      
type TracingConfiguration = {
    Tracer : ITraceSink
}
with 
    static member Default = {
        Tracer = new InMemoryTraceSink()
    }

module Trace = 
    
    let mutable private config = TracingConfiguration.Default

    let Write annotation eventType parentid spanid = config.Tracer.WriteTrace(annotation, eventType, parentid, spanid)

    let Start(cfg:TracingConfiguration option) =
        Option.iter (fun cfg -> config <- cfg) cfg

    let getConfig() = config

    let Dispose() = 
        config.Tracer.Dispose()
