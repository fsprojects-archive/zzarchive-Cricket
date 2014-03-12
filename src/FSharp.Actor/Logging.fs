namespace FSharp.Actor

open System
open FSharp.Actor.Types

module Logging = 
    
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

        { new ILogger with
            member x.Debug(msg,exn) = write "debug" (msg,exn)
            member x.Info(msg, exn) = write "info" (msg,exn)
            member x.Warning(msg, exn) = write "warn" (msg,exn)
            member x.Error(msg, exn) = write "error" (msg,exn) 
        }

    let Silent =
        { new ILogger with
            member x.Debug(msg,exn) = () |> ignore
            member x.Info(msg, exn) = () |> ignore
            member x.Warning(msg, exn) = () |> ignore
            member x.Error(msg, exn) = () |> ignore 
        }
