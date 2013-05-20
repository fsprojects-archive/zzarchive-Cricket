namespace FSharp.Actor

open System
open FSharp.Actor
open System.Collections.Generic

type ActorNotFound(message) = 
    inherit Exception(message)

module Kernel = 
    
    let mutable Logger = Logging.Console

    module Actor =
        
        let private resolveTransport scheme path = 
            match ActorTrie.tryFind (ActorPath.forTransport scheme) with
            | [] -> []
            | h::_ -> [new RemoteActorRef(path, h) :> ActorRef]
        

        let findAll (uri:string) = 
            let path = ActorPath.create uri
            match path.Scheme.ToLower() with
            | "actor" ->  ActorTrie.tryFind (ActorPath.create uri)
            | scheme -> resolveTransport scheme path

        let findOne uri = 
            match findAll uri with
            | [] -> raise(ActorNotFound(sprintf "Could not find actor %s" uri))
            | a -> a |> List.head

        

        let register (actor : ActorRef) = 
            ActorTrie.add actor.Path actor
            actor

        let remove (actor : ActorRef) =
            ActorTrie.remove actor.Path

[<AutoOpen>]
module Operators =
    
    let (!*) id = (Kernel.Actor.findAll id)
    let (!!) id = (Kernel.Actor.findOne id)
    let (<--) (ref:ActorRef) msg = ref.Post(msg)
    let (?<--) id msg = !!id <-- msg
    let (<-*) refs msg = refs |> Seq.iter (fun (a:ActorRef) -> a.Post(msg))
    let (?<-*) id msg = !*id <-* msg
    