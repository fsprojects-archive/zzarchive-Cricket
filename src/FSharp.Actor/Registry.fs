namespace FSharp.Actor 

open FSharp.Actor.Types

module Registry = 
 
    open System

    type ActorNotFound(message) = 
        inherit Exception(message)

    type TransportNotFound(message) = 
        inherit Exception(message)
    
    module Transport = 
        
        let private transports : Map<string,ITransport> ref = ref Map.empty 

        let all() = !transports |> Map.toSeq |> Seq.map snd |> Seq.toList

        let clear() = transports := Map.empty

        let tryFind id = 
            Map.tryFind id !transports

        let tryFindActorsForTransport id address =
            match tryFind id with
            | Some(t) -> [t.CreateRemoteActor(address)]
            | None -> [] 

        let register (transport:ITransport) = 
            match tryFind transport.Scheme with
            | Some _ -> failwithf "%A transport already registered" transport.Scheme
            | None -> transports := Map.add transport.Scheme transport !transports

        let remove id = 
            transports := Map.remove id !transports

    module Actor = 

        
        let private actors : Trie.trie<string, IActor> ref = ref Trie.empty

        let all() = Trie.values !actors

        let clear() = 
            actors := Trie.empty
        
        let private searchLocal address = 
             Trie.subtrie (Path.keys address) !actors |> Trie.values

        let findUnderPath (address : ActorPath) =
             match address.Scheme with
             | "actor" -> searchLocal address
             | remoteScheme -> 
                match searchLocal address with
                | [] -> Transport.tryFindActorsForTransport remoteScheme address 
                | a -> a 

        let find address = 
            match findUnderPath address with
            | [] -> raise(ActorNotFound(sprintf "Could not find actor %A" address))
            | a -> a |> List.head

        let tryFind address = 
            match findUnderPath address with
            | [] -> None
            | a -> a |> List.head |> Some

        let register (actor:IActor) = 
            actors := Trie.add (Path.keys actor.Path) actor !actors
            actor

        let remove (address:Uri) = 
            actors := Trie.remove (Path.keys address) !actors

[<AutoOpen>]
module Operators =
    
    let (!*) id = Registry.Actor.findUnderPath (Path.create id)
    let (!!) id = Registry.Actor.find (Path.create id)

    let (<-*) refs msg = refs |> Seq.iter (fun (a:IActor) -> a.Post(msg, None))
    let (<--) (ref:IActor) msg = ref.Post(msg, None)
    let (?<--) id msg = !*id <-* msg

    let (<!-) (ref:IActor) msg = ref.PostSystemMessage(msg, None)
    let (?<!-) id msg = !!id <!- msg 

