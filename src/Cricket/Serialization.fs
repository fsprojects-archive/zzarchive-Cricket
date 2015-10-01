namespace Cricket

open System.Reflection
open System.IO
open Nessos.FsPickler

type ISerializer = 
    abstract Serialize : 'a -> byte[]
    abstract Deserialize : byte[] -> 'a

type BinarySerializer() = 
    let pickler = FsPickler.CreateBinarySerializer()

    interface ISerializer with
        member x.Serialize(a:'a) = 
            use ms = new MemoryStream()
            pickler.Serialize(ms, a)
            ms.ToArray()
        member x.Deserialize(bytes) = 
            use ms = new MemoryStream(bytes)
            pickler.Deserialize(ms)

type XmlSerializer() = 
    let pickler = FsPickler.CreateXmlSerializer()

    interface ISerializer with
        member x.Serialize(a:'a) =
            use ms = new MemoryStream()
            pickler.Serialize(ms, a)
            ms.ToArray()
        member x.Deserialize(bytes) = 
            use ms = new MemoryStream(bytes)
            pickler.Deserialize(ms)