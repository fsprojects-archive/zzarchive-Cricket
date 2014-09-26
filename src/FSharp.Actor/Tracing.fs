namespace FSharp.Actor.Diagnostics

open System
open System.IO
open Nessos.FsPickler
open FSharp.Actor
open System.Threading


type TraceHeader =
    { Annotation : (string * string)[]
      Timestamp : int64 //For remoting this is not good enough need a vector or matrix clock. 
      SpanId : uint64
      ParentId : uint64 option }
    static member Empty =
        { Annotation = [||]; Timestamp = 0L; SpanId = 0UL; ParentId = None }
    static member Create(annotation, ?parentId, ?spanId) =
        let new_id = Random.randomLong()
        { Annotation = annotation; Timestamp = DateTime.UtcNow.Ticks; SpanId = defaultArg spanId new_id; ParentId = parentId }

type ITraceWriter =
    inherit IDisposable 
    abstract Write : TraceHeader -> unit

type DefaultTraceWriter(?filename, ?flushThreshold, ?token) =
    let cancelToken = defaultArg token (Async.DefaultCancellationToken)
    let flushThreshold = defaultArg flushThreshold 1000
    let fileName = (defaultArg filename (Environment.DefaultActorHostName  + ".actortrace"))
    let fileStream = new FileStream(fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.Read ||| FileShare.Delete)
    let pickler = FsPickler.CreateBinary()
    let numberOfEvents = ref 0
    let totalEvents = ref 0L

    let rec flusher() = async {
        do! Async.Sleep(200)
        if !numberOfEvents > flushThreshold
        then
            do! fileStream.FlushAsync(cancelToken)
            Interlocked.CompareExchange(numberOfEvents, 0, !numberOfEvents) |> ignore
        return! flusher()
    }

    
    let writeFileHeader() =
        let bytes = BitConverter.GetBytes(!totalEvents) 
        fileStream.Position <- 0L
        fileStream.Write(bytes, 0, bytes.Length)

    let dispose() =
        writeFileHeader()
        fileStream.Flush(true)
        fileStream.Dispose()

    do
        cancelToken.Register(fun () -> dispose()) |> ignore
        writeFileHeader()
        Async.Start(flusher(), cancelToken)

    interface ITraceWriter with
        member x.Write(header) = 
            pickler.Serialize(fileStream, header, leaveOpen = true)
            Interlocked.Increment(numberOfEvents) |> ignore
            Interlocked.Increment(totalEvents) |> ignore
        member x.Dispose() = dispose()

type TracingConfiguration = {
    IsEnabled : bool
    CancellationToken : CancellationToken
    Writer : ITraceWriter
}
with 
    static member Default = 
        {
            IsEnabled = true
            CancellationToken = Async.DefaultCancellationToken
            Writer = new DefaultTraceWriter()
        }

module Trace = 
    
    let mutable private config = TracingConfiguration.Default

    let Write annotation parentid spanid =
        if config.IsEnabled
        then config.Writer.Write(TraceHeader.Create(annotation, ?parentId = parentid, ?spanId = spanid))
    
    let Dispose() =
        config.Writer.Dispose()

    let Start(cfg:TracingConfiguration option) =
        Option.iter (fun cfg -> config <- cfg) cfg
        config.CancellationToken.Register(fun () -> Dispose())

    let getConfig() = config


    let getEventCount(file) = 
        use fileStream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read ||| FileShare.Delete)
        let buffer = Array.zeroCreate<byte> sizeof<Int64>
        fileStream.Read(buffer, 0, sizeof<Int64>) |> ignore
        BitConverter.ToInt64(buffer, 0)

    let readTraces(file) : seq<TraceHeader> = 
        seq {
            let pickler = FsPickler.CreateBinary()
            use fileStream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read ||| FileShare.Delete)
            let buffer = Array.zeroCreate<byte> sizeof<Int64>
            fileStream.Read(buffer, 0, sizeof<Int64>) |> ignore
            let events = BitConverter.ToInt64(buffer, 0)
            for i in 0L..(events - 2L) do
                  yield pickler.Deserialize(typeof<TraceHeader>, fileStream, leaveOpen = true) |> unbox
        }

