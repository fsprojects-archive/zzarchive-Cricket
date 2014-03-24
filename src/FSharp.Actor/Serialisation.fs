namespace FSharp.Actor

open System
open System.IO
open System.Runtime.Serialization.Formatters.Binary
open FSharp.Actor.Types
open FsPickler

module Serialisers = 
    
    let Binary = 
        let formatter = new BinaryFormatter()
        let isEmpty (body:byte[]) = 
            Array.forall (fun b -> b = 0uy) body
        
        let serialise o =
            use ms = new MemoryStream()
            formatter.Serialize(ms, o)
            ms.ToArray()
        
        let deserialise body = 
            if not <| isEmpty body
            then
                use ms = new MemoryStream(body) 
                formatter.Deserialize(ms)
            else null

        { new ISerialiser with
              member x.Serialise(payload) = serialise payload
              member x.Deserialise<'a>(body) = deserialise body :?> 'a
        }

    let Pickler =
        let fsp = new FsPickler()

        { new ISerialiser with
            member x.Serialise(payload) = 
                let memoryStream = new MemoryStream()
                fsp.Serialize(memoryStream, payload)
                memoryStream.ToArray()

            member x.Deserialise<'a>(body) =
                fsp.Deserialize<'a>(new MemoryStream(body))
        }




