namespace Cricket

open System
open System.Threading
open System.Collections.Concurrent

type IRegistry<'key, 'ref> =
    inherit IDisposable
    abstract Resolve : 'key -> 'ref list
    abstract ResolveAsync : 'key * TimeSpan option -> Async<'ref list>
    abstract Register : 'ref -> unit
    abstract UnRegister : 'ref -> unit
    abstract All : 'ref list with get

type ActorRegistry = IRegistry<ActorPath, ActorRef>

type InMemoryActorRegistry() =
    let syncObj = new ReaderWriterLockSlim()
    let actors : Trie<ActorRef> ref = ref Trie.empty
    interface ActorRegistry with
        member x.All with get() = !actors |> Trie.values

        member x.Resolve(path) = 
            try
                syncObj.EnterReadLock()
                let comps = ActorPath.components path
                Trie.resolve comps !actors
            finally
                syncObj.ExitReadLock()

        member x.ResolveAsync(path, _) = async { return (x :> ActorRegistry).Resolve(path) }

        member x.Register(actor) =
            try
                let components = ActorPath.components actor.Path
                syncObj.EnterWriteLock()
                actors := Trie.add components actor !actors
            finally
                syncObj.ExitWriteLock()

        member x.UnRegister actor =
            try
                let components = ActorPath.components actor.Path
                syncObj.EnterWriteLock()
                actors := Trie.remove components !actors
            finally
                syncObj.ExitWriteLock()

        member x.Dispose() =
            actors := Trie.empty

type ConcurrentDictionaryBasedRegistry<'key, 'ref>(keyGetter : 'ref -> 'key) = 
    let store = new ConcurrentDictionary<'key, 'ref>()

    interface IRegistry<'key, 'ref> with
        member x.All with get() = store.Values |> Seq.toList

        member x.Resolve(key) = 
            match store.TryGetValue(key) with
            | true, v -> [v]
            | false, _ -> []

        member x.ResolveAsync(path, _) = async { return (x :> IRegistry<'key, 'ref>).Resolve(path) }

        member x.Register(ref:'ref) =
            let key = keyGetter ref
            if store.ContainsKey(key)
            then failwithf "Failed to add %A an instance with the same key is already registered" ref
            elif store.TryAdd(key, ref)
            then ()
            else failwithf "Could not add %A to the registry" key

        member x.UnRegister ref = 
            let key = keyGetter ref
            store.TryRemove(key) |> ignore

        member x.Dispose() = 
            store.Values 
            |> Seq.iter (fun x -> 
                match (box x) with
                | :? IDisposable as v -> v.Dispose()
                | _ -> ())
            store.Clear()
