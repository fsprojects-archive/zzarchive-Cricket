namespace FSharp.Actor.Diagnostics

open System
open System.IO
open Nessos.FsPickler
open FSharp.Actor
open System.Threading
open System.Collections.Concurrent


type TraceEntry =
    { Annotation : (string * obj)[]
      Actor : ActorPath
      Sender : ActorPath
      Group : string
      Timestamp : int64 //For remoting this is not good enough need a vector or matrix clock. 
      SpanId : uint64
      ParentId : uint64 option }
    static member Empty =
        { Actor = ActorPath.empty; 
          Sender = ActorPath.empty;
          Group = String.Empty;
          Annotation = [||]; 
          Timestamp = 0L; 
          SpanId = 0UL; 
          ParentId = None }
    static member Create(actor, ?sender, ?group, ?annotation, ?parentId, ?spanId) =
        let new_id = Random.randomLong()
        { Actor = actor; 
          Sender = defaultArg sender ActorPath.empty; 
          Group = defaultArg group String.Empty;
          Annotation = defaultArg annotation [||]; 
          Timestamp = DateTime.UtcNow.Ticks; 
          SpanId = defaultArg spanId new_id; 
          ParentId = parentId }

type ITraceWriter =
    inherit IDisposable 
    abstract Write : TraceEntry -> unit

type InMemoryTraceWriter() =
    let writeQueue = new BlockingCollection<TraceEntry>()

    let dispose() = writeQueue.Dispose()

    member x.GetTraces() = writeQueue.ToArray()

    interface ITraceWriter with
        member x.Write(header) = writeQueue.Add(header)

        member x.Dispose() = dispose()

type DefaultTraceWriter(?filename, ?flushThreshold, ?maxFlushTime, ?token) =
    let cancelToken = defaultArg token (Async.DefaultCancellationToken)
    let flushThreshold = defaultArg flushThreshold 1000
    let maxFlushTime = defaultArg maxFlushTime 1000
    let fileName = (defaultArg filename (Environment.DefaultActorHostName  + ".actortrace"))
    let fileStream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read ||| FileShare.Delete)
    let pickler = FsPickler.CreateBinary()
    let writeQueue = new BlockingCollection<TraceEntry>()
    let numberOfEvents = ref 0
    let totalEvents = ref 0L

    let rec flusher() = async {
        do! Async.Sleep(maxFlushTime)
        if !numberOfEvents > flushThreshold
        then
            for i in 0..(!numberOfEvents - 1) do
                match writeQueue.TryTake() with
                | true, header -> 
                    pickler.Serialize(typeof<TraceEntry>, fileStream, header, leaveOpen = true)
                    Interlocked.Increment(totalEvents) |> ignore
                | false, _ -> ()
            do! fileStream.FlushAsync(cancelToken)
            Interlocked.CompareExchange(numberOfEvents, 0, !numberOfEvents) |> ignore
        return! flusher()
    }

    let dispose() =
        try
            //try and write any remaining events to the file. 
            for header in writeQueue.GetConsumingEnumerable() do
                 pickler.Serialize(typeof<TraceEntry>, fileStream, header, leaveOpen = true)

            fileStream.Flush(true)
        with e -> ()

        writeQueue.Dispose()
        fileStream.Flush(true)
        fileStream.Dispose()

    do
        cancelToken.Register(fun () -> dispose()) |> ignore
        Async.Start(flusher(), cancelToken)

    interface ITraceWriter with
        member x.Write(header) = 
            writeQueue.Add(header)
            Interlocked.Increment(numberOfEvents) |> ignore
        member x.Dispose() = dispose()

type TracingConfiguration = {
    IsEnabled : bool
    EnableSystemActorTracing : bool
    CancellationToken : CancellationToken
    Writer : ITraceWriter
}
with 
    static member Create(?enabled, ?systemActorTrace, ?writer, ?cancelToken) = 
        {
            IsEnabled = defaultArg enabled false
            EnableSystemActorTracing = defaultArg systemActorTrace false
            CancellationToken = defaultArg cancelToken Async.DefaultCancellationToken
            Writer = (defaultArg writer (new DefaultTraceWriter() :> ITraceWriter))
        }

module Trace = 
    
    let mutable private config = Unchecked.defaultof<_>

    let write header =
        if config.IsEnabled
        then 
            if (not config.EnableSystemActorTracing) && header.Actor.Path.StartsWith("system")
            then ()
            else config.Writer.Write(header)
    
    let dispose() =
        config.Writer.Dispose()

    let start(cfg:TracingConfiguration option) =
        match cfg with
        | Some(cfg) -> config <- cfg
        | None -> config <- TracingConfiguration.Create()

        config.CancellationToken.Register(fun () -> dispose()) |> ignore

    let readTraces(file) = 
        seq {
            let pickler = FsPickler.CreateBinary()
            use fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read ||| FileShare.Delete)
            while fileStream.Position < fileStream.Length do
                  yield pickler.Deserialize(typeof<TraceEntry>, fileStream, leaveOpen = true) |> unbox<TraceEntry>
        }

