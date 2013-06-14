namespace FSharp.Actor

module Trie =
    
    open System.Collections.Generic

    type trie<'k, 'v when 'k : comparison> = Node of 'v option * Map<'k, trie<'k, 'v>>

    let isEmpty = function
        | Node (None, m) -> Map.isEmpty m
        | _ -> false

    let empty = Node(None, Map.empty)
    
    let values trie = 
        let rec values' acc = function
            | Node(v, m) -> m |> Map.toSeq |> Seq.map snd |> Seq.fold (values') (v :: acc)
        values' [] trie |> List.choose id |> List.rev
            

    let rec tryFind keys trie = 
        match keys, trie with
        | [], Node(None, _) -> None
        | [], Node(a, _) -> a
        | k::ks, Node(_, m) -> 
            match Map.tryFind k m with
            | None -> None
            | Some(m) -> tryFind ks m

    let rec subtrie keys trie = 
        match keys, trie with
        | [], trie -> trie
        | k::ks, Node(_, m) -> 
            match Map.tryFind k m with
            | None -> empty
            | Some(m) -> subtrie ks m

    let add key value trie = 
        let rec add' = function
            | [], Node(_,m) -> Node (Some(value), m)
            | k::ks, Node(v,m) -> 
                let t' = Map.tryFind k m |> function | Some(a) -> a | None -> empty
                let t'' = add' (ks, t')
                Node(v, Map.add k t'' m)
        add' (key,trie)

    let rec remove key trie = 
        match (key,trie) with
        | [], Node (_,m) -> Node (None,m)
        | k::ks, Node (v,m) -> 
              let t' = remove ks (Map.find k m) in
              Node (v, if t' = empty then Map.remove k m else Map.add k t' m)
