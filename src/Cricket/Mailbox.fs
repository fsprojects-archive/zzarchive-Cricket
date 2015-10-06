namespace Cricket 

open System
open System.Threading
open System.Collections.Concurrent
open Cricket.Diagnostics

#if INTERACTIVE
open Cricket
open Cricket.Diagnostics
#endif

type IMailbox<'a> = 
    inherit IDisposable
    abstract Post : 'a -> unit
    abstract TryScan : int * ('a -> Async<'b> option) -> Async<Option<'b>>
    abstract Scan : ('a -> Async<'b> option) -> Async<'b>
    abstract TryReceive : int -> Async<Option<'a>>
    abstract Receive : unit -> Async<'a>

type DefaultMailbox<'a>(id : string, ?metricContext, ?boundingCapacity:int) =
    let mutable disposed = false
    let mutable inbox : ResizeArray<_> = new ResizeArray<_>()
    let mutable arrivals = 
        match boundingCapacity with
        | None -> new BlockingCollection<_>()
        | Some(cap) -> new BlockingCollection<_>(cap)
    let awaitMsg = new AutoResetEvent(false)

    let metricContext = defaultArg metricContext (Metrics.createContext id)
    let msgEnqeueMeter = Metrics.createMeter(metricContext,("msg_enqueue"))
    let msgDeqeueMeter = Metrics.createMeter(metricContext,("msg_dequeue"))
    let queueLength = Metrics.createDelegatedGuage(metricContext,"total_queue_length",(fun () -> (inbox.Count + arrivals.Count) |> int64))
    let arrivalsQueueLength = Metrics.createDelegatedGuage(metricContext,"arrivals_queue_length",(fun () -> arrivals.Count |> int64))
    let inboxQueueLength = Metrics.createDelegatedGuage(metricContext,"inbox_queue_length",(fun () -> inbox.Count |> int64))

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
             match arrivals.TryTake() with
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
            match arrivals.TryTake() with
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
        member __.TryReceive(timeout) =
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
                            do msgDeqeueMeter 1L
                            return Some(res) }
              async { match receiveFromInbox() with
                      | None -> return! await()
                      | Some res -> 
                        do msgDeqeueMeter 1L
                        return Some(res) }

        member this.Receive() =
            async {            
            let! msg = (this :> IMailbox<'a>).TryReceive(Timeout.Infinite)
            match msg with
            | Some(res) -> return res
            | None -> return raise(TimeoutException("Failed to receive message"))
            }

        member __.TryScan(timeout, f) = 
              let rec await() =
                  async { match scanArrivals(f) with
                          | None -> 
                              let! gotArrival = Async.AwaitWaitHandle(awaitMsg, timeout)
                              if gotArrival 
                              then return! await()
                              else return None
                          | Some res ->                            
                            let! msg = res
                            do msgDeqeueMeter 1L
                            return Some(msg) }
              async { match scanInbox(f, 0) with
                      | None -> return! await() 
                      | Some res ->                        
                        let! msg = res
                        do msgDeqeueMeter 1L
                        return Some(msg) }

        member this.Scan(f) =
            async {            
            let! msg = (this :> IMailbox<'a>).TryScan(Timeout.Infinite, f)
            match msg with
            | Some(res) -> return res
            | None -> return raise(TimeoutException("Failed to receive message"))
            }

        member __.Post(msg) = 
            if disposed 
            then ()
            else
                arrivals.Add(msg)
                msgEnqeueMeter 1L
                awaitMsg.Set() |> ignore

        member __.Dispose() = 
            inbox <- null
            disposed <- true

