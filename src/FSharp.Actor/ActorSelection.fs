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
            | :? list<ActorRef> as ref -> ref |> ActorSelection
            | :? array<ActorRef> as ref -> ref |> Array.toList |> ActorSelection
            | :? seq<ActorRef> as ref -> ref |> Seq.toList |> ActorSelection
            | :? ActorSelection as sel -> sel
            | :? Lazy<ActorSelection> as sel -> sel.Value
            | :? list<ActorSelection> as ref -> ref |> List.collect (fun (ActorSelection d) -> d) |> ActorSelection
            | :? array<ActorSelection> as ref -> ref |> Array.toList |> List.collect (fun (ActorSelection d) -> d) |> ActorSelection
            | :? seq<ActorSelection> as ref -> ref |> Seq.toList |> List.collect (fun (ActorSelection d) -> d) |> ActorSelection 
            | _ -> failwithf "Cannot convert %A to an ActorSelection" s     