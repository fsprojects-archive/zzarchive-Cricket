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
    abstract TryScan : int * ('a -> Async<'b> option) -> Async<Option<'b>>
    abstract Scan : int * ('a -> Async<'b> option) -> Async<'b>
    abstract TryReceive : int -> Async<Option<'a>>
    abstract Receive : int -> Async<'a>

type DefaultMailbox<'a>(id : string, ?metricContext, ?boundingCapacity:int) =
    let mutable disposed = false
    let mutable inbox : ResizeArray<_> = new ResizeArray<_>()
    let mutable arrivals = 
        match boundingCapacity with
        | None -> new ConcurrentQueue<_>()
        | Some(cap) -> new ConcurrentQueue<_>(new BlockingCollection<_>(cap))
    let awaitMsg = new AutoResetEvent(false)

    let metricContext = defaultArg metricContext (Metrics.createContext id)
    let msgEnqeueMeter = Metrics.createMeter metricContext ("msg_enqueue")
    let msgDeqeueMeter = Metrics.createMeter metricContext ("msg_dequeue")
    let queueLength = Metrics.createDelegatedGuage metricContext "msg_queue_length" (fun () -> (inbox.Count + arrivals.Count) |> int64)

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
        member this.TryReceive(timeout) =
              let rec await() =
                  async { match receiveFromArrivals() with
                          | None -> 
                              let! gotArrival = Async.AwaitWaitHandle(awaitMsg, timeout)
                              if gotArrival 
                              then
                                return! await()
                              else
                                return None
                          | Some res -> 
                            do msgDeqeueMeter.Mark 1L
                            return Some(res) }
              async { match receiveFromInbox() with
                      | None -> return! await()
                      | Some res -> 
                        do msgDeqeueMeter.Mark 1L
                        return Some(res) }

        member this.Receive(timeout) =
            async {            
            let! msg = (this :> IMailbox<'a>).TryReceive(timeout)
            match msg with
            | Some(res) -> return res
            | None -> return raise(TimeoutException("Failed to receive message"))
            }

        member this.TryScan(timeout, f) = 
              let rec await() =
                  async { match scanArrivals(f) with
                          | None -> 
                              let! gotArrival = Async.AwaitWaitHandle(awaitMsg, timeout)
                              if gotArrival 
                              then return! await()
                              else return None
                          | Some res ->                            
                            let! msg = res
                            do msgDeqeueMeter.Mark 1L
                            return Some(msg) }
              async { match scanInbox(f, 0) with
                      | None -> return! await() 
                      | Some res ->                        
                        let! msg = res
                        do msgDeqeueMeter.Mark 1L
                        return Some(msg) }

        member this.Scan(timeout, f) =
            async {            
            let! msg = (this :> IMailbox<'a>).TryScan(timeout, f)
            match msg with
            | Some(res) -> return res
            | None -> return raise(TimeoutException("Failed to receive message"))
            }

        member this.Post(msg) = 
            if disposed 
            then ()
            else
                arrivals.Enqueue(msg)
                msgEnqeueMeter.Mark 1L
                awaitMsg.Set() |> ignore

        member this.Dispose() = 
            inbox <- null
            disposed <- true

