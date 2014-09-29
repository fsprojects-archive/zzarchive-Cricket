#I "../../bin"
#r "FSharp.Actor.dll"

open System
open FSharp.Actor

let traceFile = @"D:\Appdev\Fsharp.Actor\src\Samples\Remoting\PingPong\PingNode\bin\Debug\HP20024950_PingNode_6744.actortrace"

let traces = Diagnostics.Trace.readTraces(traceFile)

fsi.AddPrinter(fun (x:System.DateTime) -> x.ToString("dd/MM/yyyy HH:mm:ss.fffffff"))
fsi.AddPrintTransformer(fun (x:Diagnostics.TraceHeader) -> (x.Annotation, x.ParentId, x.SpanId, System.DateTime(x.Timestamp)) |> box)
fsi.AddPrintTransformer(fun (x:(uint64 option * uint64 * seq<Diagnostics.TraceHeader>)) -> (x |> fst, x |> snd |> Seq.toList) |> box)

let annotationsToStr annos = 
    String.Join("\r\n", annos |> Seq.map (fun (k,v) -> sprintf "%s = %s" k v) |> Seq.toArray)

let timeLine = 
    traces
    |> Seq.toList
    |> Seq.groupBy (fun t -> t.ParentId, t.SpanId)
    |> Seq.map (fun (p, ts) -> p, ts)
    |> Seq.take 100
    |> Seq.toList
