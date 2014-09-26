namespace FSharp.Actor

open System
open FSharp.Actor

type ActorSelection = 
    | ActorSelection of ActorRef list
    with
        member internal x.Refs 
            with get() = 
                let (ActorSelection xs) = x 
                xs
        static member op_Implicit(s:'a) =
            match box s with
            | :? string as str -> 
                ActorPath.ofString str
                |> ActorHost.Instance.ResolveActor 
                |> ActorSelection
            | :? ActorPath as path -> 
                ActorHost.Instance.ResolveActor path 
                |> ActorSelection
            | :? ActorRef as ref -> [ref] |> ActorSelection
            | :? ActorSelection as sel -> sel
            | :? Lazy<ActorSelection> as sel -> sel.Value
            | _ -> failwithf "Cannot convert %A to an ActorSelection" s      