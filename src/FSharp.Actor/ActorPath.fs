namespace FSharp.Actor

open System
open FSharp.Actor.Types

module Path = 
    
    let toLocal (path:ActorPath) = 
        new ActorPath(sprintf "actor://%s/%s" Environment.MachineName path.PathAndQuery)

    let create (path:string) = 
        match path.ToLower() with
        //TODO: This is crap need to come up with a better scheme than this
        | id when id.StartsWith("actor") ->
            new ActorPath(id)
        | id -> 
            new ActorPath(sprintf "actor://%s/%s" Environment.MachineName (id.TrimStart('/')))
            

    let keys (address:ActorPath) =
        let segs = address.AbsoluteUri.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries)
        segs |> Array.map (fun x -> x.Replace(":", "")) |> List.ofArray

