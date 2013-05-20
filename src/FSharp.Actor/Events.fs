namespace FSharp.Actor

module Events = 
    
    module Actor = 
        type T = {
            PreStart : (ActorRef -> unit) list
            PreRestart : (ActorRef -> unit) list
            PreStop : (ActorRef -> unit) list
            OnStopped : (ActorRef -> unit) list
            OnStarted : (ActorRef -> unit) list
            OnRestarted : (ActorRef -> unit) list
        }

        let Default = {
                PreStart = [(fun a -> Kernel.Logger.Debug(sprintf "%A pre-start Status: %A" a a.Status))]
                PreStop = [(fun a -> Kernel.Logger.Debug(sprintf "%A pre-stop Status: %A" a a.Status))]
                PreRestart = [(fun a -> Kernel.Logger.Debug(sprintf "%A pre-restart Status: %A" a a.Status))]
                OnStarted  = [(fun a -> Kernel.Logger.Debug(sprintf "%A started Status: %A" a a.Status))]
                OnStopped  = [(fun a -> Kernel.Logger.Debug(sprintf "%A stopped Status: %A" a a.Status))]
                OnRestarted  = [(fun a -> Kernel.Logger.Debug(sprintf "%A re-started Status: %A" a a.Status))]
            }

        let run evnts actor = 
            evnts |> List.iter (fun f -> f actor) 

