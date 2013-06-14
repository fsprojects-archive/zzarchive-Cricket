namespace FSharp.Actor

open System
open System.Collections.Concurrent
open System.Threading
open FSharp.Actor.Types

type DefaultMailbox<'a>() =
    let mutable inbox = ConcurrentQueue<'a>()
    let awaitMsg = new AutoResetEvent(false)

    let rec await timeout cancellationToken = async {
       match inbox.TryDequeue() with
       | true, msg -> 
          return msg
       | false, _ -> 
          let! recd = Async.AwaitWaitHandle(awaitMsg, timeout)
          if recd
          then return! await timeout cancellationToken   
          else return raise(TimeoutException("Receive timed out"))     
    }
    
    interface IMailbox<'a> with  
        member this.Receive(timeout, cancellationToken) = await (defaultArg timeout Timeout.Infinite) cancellationToken
        member this.Post(msg) = 
            inbox.Enqueue(msg)
            awaitMsg.Set() |> ignore
        member this.Length with get() = inbox.Count
        member this.IsEmpty with get() = inbox.IsEmpty
        member x.Dispose() = 
            awaitMsg.Dispose()
            inbox <- null
        member x.Restart() = inbox <- ConcurrentQueue()


