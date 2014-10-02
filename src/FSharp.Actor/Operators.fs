namespace FSharp.Actor

#if INTERACTIVE
open FSharp.Actor
#endif

[<AutoOpen>]
module ActorOperators =
 
    let inline (!!) a = ActorSelection.op_Implicit a
    
    let inline (!~) a = lazy !!a  
    
    let inline (-->) msg t = 
        Message.postMessage t { Id = Some (Random.randomLong()); Sender = Null; Message = msg }
    
    let inline (<--) t msg =
        Message.postMessage t { Id = Some (Random.randomLong()); Sender = Null; Message = msg }

