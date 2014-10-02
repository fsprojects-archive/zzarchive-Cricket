namespace FSharp.Actor

open System
open FSharp.Actor

type ActorSelection = 
    | ActorSelection of ActorRef list
    with
        member internal x.Refs 
            with get() = let (ActorSelection xs) = x in xs
        static member op_Implicit(s:'a) =
            match box s with
            | :? string as str -> 
                ActorPath.ofString str
                |> ActorHost.Instance.ResolveActor 
                |> ActorSelection
            | :? seq<string> as strs -> 
                strs |> Seq.toList |> List.collect (fun str -> ActorPath.ofString str |> ActorHost.Instance.ResolveActor ) |> ActorSelection
            | :? ActorPath as path -> 
                ActorHost.Instance.ResolveActor path 
                |> ActorSelection
            | :? seq<ActorPath> as paths -> 
                paths |> Seq.toList |> List.collect (ActorHost.Instance.ResolveActor) |> ActorSelection 
            | :? ActorRef as ref -> [ref] |> ActorSelection
            | :? seq<ActorRef> as ref -> ref |> Seq.toList |> ActorSelection
            | :? ActorSelection as sel -> sel
            | :? Lazy<ActorSelection> as sel -> sel.Value
            | :? seq<ActorSelection> as ref -> ref |> Seq.toList |> List.collect (fun (ActorSelection d) -> d) |> ActorSelection 
            | _ -> failwithf "Cannot convert %A to an ActorSelection" s

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ActorSelection = 
    
    let empty = ActorSelection([])

    let map f (ActorSelection ss) = ActorSelection(List.map f ss)

    let cons ref (ActorSelection ss) = ActorSelection(ref :: ss)

    let filter f (ActorSelection ss) = ActorSelection(List.filter f ss)