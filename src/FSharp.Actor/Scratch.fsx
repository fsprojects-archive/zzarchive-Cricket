
#load "Math.fs"
open System
open FSharp.Actor

module Trace = 

    type Span =
      { Annotations : string[]
        Timestamp : int64 
        SpanId : uint64
        ParentId : uint64 option }
      static member Empty =
        { Annotations = [||]; Timestamp = 0L; SpanId = 0UL; ParentId = None }
      static member Create(annotations, ?parentId, ?spanId) =
        let new_id = Random.randomLong()
        { Annotations = annotations; Timestamp = DateTime.UtcNow.Ticks; SpanId = defaultArg spanId new_id; ParentId = parentId }

    let write (span:Span) = 
        printfn "Trace: %A" span

type Context<'a> = {
    Name : string
    Receive : (unit -> Async<'a>)
}
with
    static member Default = 
        {
            Name = "some/actor"
            Receive = (fun () -> 
                async { 
                    do! Async.Sleep(2000)
                    return "Some message"
                })
        }


type MessageHandler<'a> = MH of (Context<'a> -> Async<'a>)

let bind (MH handler) (f : 'a -> MessageHandler<'a>) = 
    MH (fun context -> 
         async {
            let! comp = handler context
            Trace.write (Trace.Span.Create([|context.Name + "_start"|]))
            let (MH nextComp) = f comp
            Trace.write (Trace.Span.Create([|context.Name + "_end"|]))
            return! nextComp context
         } 
    ) 

type MessageHandlerBuilder() = 
    member x.Bind(m,f) = bind m f
    member x.Return(m) = MH(fun ctx -> m)
    member x.ReturnFrom(m) = m


let messageHandler = new MessageHandlerBuilder()

let getMessage f = MH (fun ctx -> f ctx)

let startHandler (MH handler) ctx = Async.Start(handler ctx |> Async.Ignore)



let rec newMessageLoop() = 
    messageHandler {
        let! msg = getMessage (fun ctx -> ctx.Receive())
        printfn "Got message %s" msg
        return! secondMessageLoop()
    }

and secondMessageLoop() = 
    messageHandler {
        let! msg = getMessage (fun ctx -> ctx.Receive())
        printfn "Another message %s" msg
        return! newMessageLoop()
    }

startHandler (newMessageLoop()) (Context<string>.Default)