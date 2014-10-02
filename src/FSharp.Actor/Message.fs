namespace FSharp.Actor 

open System
open System.Threading
open FSharp.Actor
open FSharp.Actor.Diagnostics

#if INTERACTIVE
open FSharp.Actor
open FSharp.Actor.Diagnostics
#endif

type InvalidMessageException(payload:obj, innerEx:Exception) =
    inherit Exception("Unable to handle msg", innerEx)
    member val Buffer = payload with get

type MessageHandler<'a, 'b> = MH of (ActorCell<'a> -> Async<'b>)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Message = 

    let emptyHandler = 
        (MH (fun _ -> 
         let rec loop() = 
             async { return! loop() }
         loop()))
    
    let private traceHandled (context:ActorCell<_>) =
        Trace.write (TraceEntry.Create(context.Self.Path, 
                               context.Sender.Path, 
                               "message_handled",
                               ?parentId = context.ParentId, 
                               spanId = context.SpanId))

    let private traceReceive (context:ActorCell<_>) =
        Trace.write (TraceEntry.Create(context.Self.Path, 
                               context.Sender.Path, 
                               "message_receive",
                               ?parentId = context.ParentId, 
                               spanId = context.SpanId))
    
    let receive timeout = MH (fun ctx -> async {
        let! msg = ctx.Receive(?timeout = timeout)
        ctx.Sender <- msg.Sender
        ctx.ParentId <- msg.Id
        ctx.SpanId <- Random.randomLong()
        traceReceive ctx
        return msg.Message  
    })
    
    let sender() = MH (fun ctx -> async { return ActorSelection.op_Implicit ctx.Sender }) 

    let tryReceive timeout = MH (fun ctx -> async {
        let! msg = ctx.TryReceive(?timeout = timeout)
        return Option.map (fun (msg:Message<_>)-> 
            ctx.Sender <- msg.Sender
            ctx.ParentId <- msg.Id
            ctx.SpanId <- Random.randomLong()
            traceReceive ctx 
            msg.Message) msg 
    })

    let scan timeout f = MH (fun ctx -> async {
        let! (msg:Message<_>) = ctx.Scan(f, ?timeout = timeout)
        ctx.Sender <- msg.Sender
        ctx.ParentId <- msg.Id
        ctx.SpanId <- Random.randomLong()
        traceReceive ctx
        return msg.Message  
    })

    let tryScan timeout f = MH (fun ctx -> async {
        let! msg = ctx.TryScan(f, ?timeout = timeout)
        return Option.map (fun (msg:Message<_>) -> 
            ctx.Sender <- msg.Sender
            ctx.ParentId <- msg.Id
            ctx.SpanId <- Random.randomLong()
            traceReceive ctx
            msg.Message) msg
    })

    let postMessage (targets:'a) (msg:Message<obj>) =
        (ActorSelection.op_Implicit targets).Refs 
        |> List.iter (fun target ->
                        match target with
                        | ActorRef(target) -> target.Post(msg)
                        | Null -> ())

    let post targets msg =
        MH (fun ctx -> async {
                let ref = (ActorRef ctx.Self)
                do postMessage targets { Id = Some ctx.SpanId; Sender = ref; Message = msg }
            }
        )

    let reply msg = MH (fun ctx -> async {
        let ref =  (ActorRef ctx.Self)
        let targets = (ActorSelection([ctx.Sender]))
        do postMessage targets { Id = ctx.ParentId; Sender = ref; Message = msg }
    })

    let toAsync (MH handler) ctx = handler ctx |> Async.Ignore

    type MessageHandlerBuilder() = 
        member __.Bind(MH handler,f) =
             MH (fun context -> 
                  async {
                     let! comp = handler context
                     let (MH nextComp) = f comp
                     return! nextComp context
                  } 
             ) 
        member __.Bind(a:Async<_>, f) = 
            MH (fun context -> 
                async {
                     let! comp = a
                     let (MH nextComp) = f comp
                     return! nextComp context
                  } 
            )
        member __.Return(m) = 
            MH(fun ctx -> 
                traceHandled ctx; 
                async { return m } )
        member __.ReturnFrom(MH m) = 
            MH(fun ctx -> 
                traceHandled ctx; 
                m(ctx))
        member __.Zero() = MH(fun _ -> async.Zero())
        member x.Delay(f) = x.Bind(x.Zero(), f)
        member __.Using(r,f) = MH(fun ctx -> use rr = r in let (MH g) = f rr in g ctx)
        member x.Combine(c1 : MessageHandler<_,_>, c2) = x.Bind(c1, fun () -> c2)
        member x.For(sq:seq<'a>, f:'a -> MessageHandler<'b, 'c>) = 
          let rec loop (en:System.Collections.Generic.IEnumerator<_>) = 
            if en.MoveNext() then x.Bind(f en.Current, fun _ -> loop en)
            else x.Zero()
          x.Using(sq.GetEnumerator(), loop)

        member x.While(t, f:unit -> MessageHandler<_, unit>) =
          let rec loop () = 
            if t() then x.Bind(f(), loop)
            else x.Zero()
          loop()

