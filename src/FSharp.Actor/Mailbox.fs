namespace FSharp.Actor 

open System
open System.Threading
open System.Collections.Concurrent

#if INTERACTIVE
open FSharp.Actor
#endif

type IMailbox<'a> = 
    inherit IDisposable
    abstract Post : 'a -> unit
    abstract Scan : int * ('a -> Async<'b> option) -> Async<'b>
    abstract Receive : int -> Async<'a>

type DefaultMailbox<'a>() =
    let mutable disposed = false
    let mutable inbox : ResizeArray<_> = new ResizeArray<_>()
    let mutable arrivals = new ConcurrentQueue<_>()
    let awaitMsg = new AutoResetEvent(false)
    
    let rec scanInbox(f,n) =
        match inbox with
        | null -> None
        | inbox ->
            if n >= inbox.Count
            then None
            else
                let msg = inbox.[n]
                match f msg with
                | None -> scanInbox (f,n+1)
                | res -> inbox.RemoveAt(n); res

    let rec scanArrivals(f) =
        if arrivals.Count = 0 then None
        else 
             match arrivals.TryDequeue() with
             | true, msg -> 
                 match f msg with
                 | None -> 
                     inbox.Add(msg); 
                     scanArrivals(f)
                 | res -> res
             | false, _ -> None

    let receiveFromArrivals() =
        if arrivals.Count = 0 
        then None
        else
            match arrivals.TryDequeue() with
            | true, msg -> Some msg
            | false, _ -> None

    let receiveFromInbox() =
        match inbox with
        | null -> None
        | inbox ->
            if inbox.Count = 0
            then None
            else
                let x = inbox.[0]
                inbox.RemoveAt(0);
                Some(x)

    interface IMailbox<'a> with
        member this.Receive(timeout) =
              let rec await() =
                  async { match receiveFromArrivals() with
                          | None -> 
                              let! gotArrival = Async.AwaitWaitHandle(awaitMsg, timeout)
                              if gotArrival 
                              then return! await()
                              else return raise(TimeoutException("Failed to receive message"))
                          | Some res -> return res }
              async { match receiveFromInbox() with
                      | None -> return! await() 
                      | Some res -> return res }
        
        member this.Scan(timeout, f) = 
              let rec await() =
                  async { match scanArrivals(f) with
                          | None -> 
                              let! gotArrival = Async.AwaitWaitHandle(awaitMsg, timeout)
                              if gotArrival 
                              then return! await()
                              else return raise(TimeoutException("Failed to receive message"))
                          | Some res -> return! res }
              async { match scanInbox(f, 0) with
                      | None -> return! await() 
                      | Some res -> return! res }

        member this.Post(msg) = 
            if disposed 
            then ()
            else
                arrivals.Enqueue(msg)
                awaitMsg.Set() |> ignore

        member this.Dispose() = 
            inbox <- null
            disposed <- true

