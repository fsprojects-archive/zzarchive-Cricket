namespace FSharp.Actor

module ActorTrie = 
    
    open System

    let instance : Trie.trie<string, ActorRef> ref = ref Trie.empty

    let empty : Trie.trie<string, ActorRef> = Trie.empty

    let clear() = instance := empty

    let tryFind (path : ActorPath.T) =
         Trie.subtrie path.Keys (!instance)
         |> Trie.values

    let add (path:ActorPath.T) actorRef = 
        instance := Trie.add path.Keys actorRef !instance
    
    let addActor (actor:ActorRef) =
        instance := Trie.add actor.Path.Keys actor !instance

    let remove (path:ActorPath.T) = 
        instance := Trie.remove path.Keys !instance 

