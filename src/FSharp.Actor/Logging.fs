namespace FSharp.Actor

open System
open System.Collections.Concurrent

module Logging = 
    
    type Adapter = {
        DebugF : string * exn option -> unit
        InfoF : string * exn option -> unit
        WarningF : string * exn option -> unit
        ErrorF : string * exn option -> unit
    }
    with
        member x.Debug(msg, ?exn) = x.DebugF(msg, exn)
        member x.Info(msg, ?exn) = x.InfoF(msg, exn)
        member x.Warning(msg, ?exn) = x.WarningF(msg, exn)
        member x.Error(msg, ?exn) = x.ErrorF(msg, exn)


    let Console =
        let write level (msg,exn : exn option) =
            let msg = 
                match exn with
                | Some(err) ->
                    String.Format("{0} [{1}]: {2} : {3}\n{4}", 
                                       DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss.fff"), 
                                       level,
                                       msg, err.Message, err.StackTrace)
                | None ->
                     String.Format("{0} [{1}]: {2}", 
                                       DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm:ss.fff"), 
                                       level,
                                       msg)
            match level with
            | "info" -> Console.ForegroundColor <- ConsoleColor.Green  
            | "warn" -> Console.ForegroundColor <- ConsoleColor.Yellow
            | "error" -> Console.ForegroundColor <- ConsoleColor.Red
            | _ -> Console.ForegroundColor <- ConsoleColor.White 
            Console.WriteLine(msg)
            Console.ForegroundColor <- ConsoleColor.White

        {
            DebugF = write "debug"
            InfoF = write "info"
            WarningF = write "warn"
            ErrorF = write "error"
        }

