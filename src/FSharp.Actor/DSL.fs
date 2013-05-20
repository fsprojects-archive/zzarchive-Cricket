namespace FSharp.Actor

module DSL =
    
    open FSharp.Actor

    module Actor = 
        
        let named (name) = 
            ActorConfig<'a>.Create(name)
        
        let timeoutAfter (timeout) (config:ActorConfig<_>) =  
            { config with RecieveTimeout = Some timeout }

        let initialBehaviour behaviour (config:ActorConfig<_>) =
            (config.Behaviours.Push(behaviour)) 
            config

        let recieveFrom mailBox (config : ActorConfig<_>) =
            { config with Mailbox = mailBox }

        let register config = 
            let actor = new Actor<_>(config)
            Kernel.Actor.register (actor.Ref())

        let create name behaviour = 
            named name
            |> initialBehaviour behaviour

        let link linkees (actor:ActorRef) = 
            linkees |> Seq.iter (fun l -> actor.Link(l))
            actor

        let unlink linkees (actor:ActorRef) =
            linkees |> Seq.iter (fun l -> actor.Unlink(l))
            actor

        let spawnAs name behaviour f = 
            f(create name behaviour) |> register

        let spawn name behaviour = 
            spawnAs name behaviour id

        let spawnLinked name behavior linkees =
            link linkees (spawn name behavior)

        let supervisedBy sup (watchee : ActorRef) =
            watchee.Watch(sup)
            watchee
        
        let unwatch (actors:seq<ActorRef>) = 
            actors |> Seq.iter (fun a -> a.Unwatch())

        let supervisor name strategy (actors:ActorRef seq) = 
            let sup = (named name) 
            sup.Supervisor.SetStrategy(strategy) 
            let sup = sup |> register
            actors |> Seq.iter (fun a -> a.Watch(sup))
            sup
            




