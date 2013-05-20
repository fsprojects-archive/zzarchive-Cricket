namespace FSharp.Actor

module Supervisor =
    
    type Strategy = 
        {
             Handle : ActorRef -> SupervisorMessage -> unit
        }
        with 
            static member Empty = { Handle = (fun _ _ -> ()) }

    type Config = 
        {
            mutable Supervisor : ActorRef option
            mutable Strategy : Strategy
        }
        with
          static member Empty =  { Supervisor = None; Strategy = Strategy.Empty }
          member x.Notify(msg) = 
                x.Supervisor |> Option.map (fun sup -> sup.Post(Supervisor(msg)))
          member x.Watch(actor, supervisor) =
                x.Supervisor <- Some(supervisor)
                supervisor.Link(actor)
          member x.Unwatch(actor) =
                x.Supervisor |> Option.iter (fun s -> s.Unlink(actor))
                x.Supervisor <- None
          member x.Handle(supervisor, msg) = x.Strategy.Handle supervisor msg
          member x.SetStrategy(s) = 
                x.Strategy <- s

    let Fail = 
        {
            Handle = fun supervisor msg ->
                        match msg with 
                        | Errored(actorRef, exn) -> 
                           Kernel.Logger.Debug(sprintf "Terminating (AlwaysTerminate: %A) due to error %A" actorRef exn)
                           actorRef.Post(Die)
                        | Terminated(actorRef) ->
                            Kernel.Logger.Debug(sprintf "Supervised actor terminated: %A" actorRef)
        }

    let OneForOne = 
        {
            Handle = fun supervisor msg ->
                        match msg with 
                        | Errored(actorRef, exn) -> 
                           Kernel.Logger.Debug(sprintf "Restarting (OneForOne: %A) due to error %A" actorRef exn)
                           actorRef.Post(Restart)
                        | Terminated(actorRef) ->
                            Kernel.Logger.Debug(sprintf "Supervised actor terminated: %A" actorRef)
        }

    let OneForAll = 
        {
            Handle = fun supervisor msg -> 
                        match msg with
                        | Errored(actorRef, exn) ->
                           Kernel.Logger.Debug(sprintf "Restarting (OneForAll %A) due to error %A" actorRef exn) 
                           supervisor.Children |> Seq.iter (fun a -> a.Post(Restart))
                        | Terminated(actorRef) ->
                           Kernel.Logger.Debug(sprintf "Supervised actor terminated: %A" actorRef)
        }

