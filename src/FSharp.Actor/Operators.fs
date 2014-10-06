namespace FSharp.Actor

#if INTERACTIVE
open FSharp.Actor
#endif

[<AutoOpen>]
module ActorOperators =
    
    let inline resolve a = ActorSelection.op_Implicit a

    let inline resolveLazy a = lazy (resolve a)
    
    let inline post target msg =
        Message.postMessage target { Id = Some (Random.randomLong()); Sender = Null; Message = msg }

    let inline (!!) a = resolve a
    
    let inline (!~) a = resolveLazy a 
    
    let inline (-->) msg t = post t msg

    let inline (<--) t msg = post t msg

