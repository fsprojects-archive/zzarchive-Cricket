namespace FSharp.Actor

open Nessos.FsPickler

type ISerializer = 
    abstract Serialize : 'a -> byte[]
    abstract Deserialize : byte[] -> 'a

type FsPicklerSerializer() = 
    let pickler = FsPickler.CreateBinary()

    interface ISerializer with
        member x.Serialize(a:'a) = pickler.Pickle(a)
        member x.Deserialize(bytes) = pickler.UnPickle(bytes)


