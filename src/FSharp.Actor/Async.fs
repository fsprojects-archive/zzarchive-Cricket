namespace FSharp.Actor

[<AutoOpen>]
module AsyncExtensions =
    open System
    open System.Threading
    open System.Threading.Tasks
    
    module internal AsyncHelpers =
      let invokeOnce funcs =
        let counter = ref 0
        let invokeOnce' f x =
          if (Interlocked.CompareExchange (counter, 1, 0) = 0) then
            f x
        let (a, b, c) = funcs
        (invokeOnce' a, invokeOnce' b, invokeOnce' c)
    
    open AsyncHelpers
    
    
    type Microsoft.FSharp.Control.Async with
      static member Map f comp = async {
        let! r = comp
        return f r }
      static member AsyncMap f comp = async {
        let! r = comp
        return! f r }
    
      static member WithTimeout(timeout : TimeSpan, computation : 'a Async) : 'a Async =
        let callback (success, error, cancellation) =
          let (success, error, cancellation) = invokeOnce (success, error, cancellation)
          let fetchResult = async {
            let! result = computation
            success result }
          let timeoutExpired = async {
            do! Async.Sleep (int timeout.TotalMilliseconds)
            let ex = new TimeoutException ("Timeout expired") :> Exception
            error ex }
    
          Async.StartImmediate fetchResult
          Async.StartImmediate timeoutExpired
    
        Async.FromContinuations callback
    
      static member Raise (e : #exn) =
        Async.FromContinuations(fun (_,econt,_) -> econt e)
    
      static member AwaitTask (t : Task) =
        let flattenExns (e : AggregateException) = e.Flatten().InnerExceptions |> Seq.nth 0
        let rewrapAsyncExn (it : Async<unit>) =
          async { try do! it with :? AggregateException as ae -> do! (Async.Raise <| flattenExns ae) }
        let tcs = new TaskCompletionSource<unit>(TaskCreationOptions.None)
        t.ContinueWith((fun t' ->
          if t.IsFaulted then tcs.SetException(t.Exception |> flattenExns)
          elif t.IsCanceled then tcs.SetCanceled ()
          else tcs.SetResult(())), TaskContinuationOptions.ExecuteSynchronously)
        |> ignore
        tcs.Task |> Async.AwaitTask |> rewrapAsyncExn
    
    /// Implements an extension method that overloads the standard
    /// 'Bind' of the 'async' builder. The new overload awaits on
    /// a standard .NET task
    type Microsoft.FSharp.Control.AsyncBuilder with
      member x.Bind(t:Task<'T>, f:'T -> Async<'R>) : Async<'R> = async.Bind(Async.AwaitTask t, f)
      member x.Bind(t:Task, f:unit -> Async<'R>) : Async<'R> = async.Bind(Async.AwaitTask t, f)
    
    type System.IO.Stream with
        member x.WriteBytesAsync (bytes : byte []) =
            async {
                do! x.WriteAsync(BitConverter.GetBytes bytes.Length, 0, 4)
                do! x.WriteAsync(bytes, 0, bytes.Length)
                do! x.FlushAsync()
            }
    
        member x.ReadBytesAsync(length : int) =
            let rec readSegment buf offset remaining =
                async {
                    let! read = x.ReadAsync(buf, offset, remaining)
                    if read < remaining then
                        return! readSegment buf (offset + read) (remaining - read)
                    else
                        return ()
                }
    
            async {
                let bytes = Array.zeroCreate<byte> length
                do! readSegment bytes 0 length
                return bytes
            }
    
        member x.ReadBytesAsync() =
            async {
                let! lengthArr = x.ReadBytesAsync 4
                let length = BitConverter.ToInt32(lengthArr, 0)
                return! x.ReadBytesAsync length
            }
    
open System
open System.Threading.Tasks

type AsyncResultCell<'a>() =
  let source = new TaskCompletionSource<'a>()

  member x.Complete result =
    source.SetResult(result)

  member x.AwaitResult(?timeout : TimeSpan) = async {
    match timeout with
    | None ->
      let! res = source.Task
      return Some res
    | Some time ->
      try
        let! res = Async.WithTimeout(time, Async.AwaitTask(source.Task))
        return Some res
      with
      | :? TimeoutException as e ->
        return None }

  member x.Result () = source.Task.Result



