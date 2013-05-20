namespace FSharp.Actor

open System
open System.Net

module ActorPath =
    
    type T(uri : Uri) = 
        let path = uri
        
        let lookupPaths = 
            path.Segments.[1..]
            |> List.ofArray
            |> List.fold (fun (s,k') k ->
                               let k = k.Trim('/') 
                               let acc = if k <> "" then (k::k') else k'
                               (acc |> List.rev)::s, acc
                         ) ([[path.Segments.[0]]], [])
            |> fst
            |> List.rev
    
        let keys = path.Segments |> List.ofArray
    
        new(?id) =
           match id with
           | Some("/") -> new T(new Uri(sprintf "actor://%s/" Environment.MachineName))
           | Some(id) when id.StartsWith("actor") -> new T(new Uri(id.ToLower()))
           | id -> new T(new Uri(sprintf "actor://%s/%s" Environment.MachineName ((defaultArg id (Guid.NewGuid().ToString())).ToLower()))) 
    
        member x.Scheme = path.Scheme
        member x.AbsoluteUri = path.AbsoluteUri
        member x.AbsolutePath = path.AbsolutePath.TrimStart([|'/'|])
        member x.LookupPaths = lookupPaths
        member x.Keys = keys
        member x.IpEndpoint = new IPEndPoint(IPAddress.Parse(path.Host), path.Port) //TODO: Should work of machine name
        override x.ToString() = path.AbsoluteUri

    let TransportPrefix = "transports"
    let SystemPrefix = "system"
    let RemotingSupervisor = new T(SystemPrefix + "/remotingsupervisor")
    
    let ofUri (uri:Uri) = new T(uri)

    let create (id:string) = new T(id)

    let forTransport scheme = 
        create (TransportPrefix + "/" + scheme)



