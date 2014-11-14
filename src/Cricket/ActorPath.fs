namespace Cricket

open System
open System.Net
open System.Net.NetworkInformation
open System.Net.Sockets
open Cricket

type private actorPathComponent =
    | Transport of string
    | Host of string
    | MachineAddress of string
    | Port of int
    | PathComponent of string[]

type ActorPath = {
    Transport : string option
    Host : string option
    MachineAddress : string option
    Port : int option
    MachineAddressType : UriHostNameType option
    PathComponents : string[]
}
with
    member x.Path with get() =  String.Join("/", x.PathComponents)
         
    override x.ToString() =
        match x.Port with
        | Some(port) when port > -1 -> 
            sprintf "%s://%s@%s:%d/%s" (defaultArg x.Transport "actor") (defaultArg x.Host "*") (defaultArg x.MachineAddress "*") port  (String.Join("/", x.PathComponents))
        | _ -> 
            sprintf "%s://%s@%s/%s" (defaultArg x.Transport "actor") (defaultArg x.Host "*") (defaultArg x.MachineAddress "*") (String.Join("/", x.PathComponents))
    
    member x.IsAbsolute
            with get() =  
                x.Transport.IsSome
                && x.MachineAddress.IsSome
            
    static member internal Create(path:string, ?transport, ?system, ?host, ?port, ?hostType) = 
        { 
            Transport = Option.stringIsNoneIfBlank transport
            Host = Option.stringIsNoneIfBlank system 
            MachineAddress = Option.stringIsNoneIfBlank host 
            Port = port
            MachineAddressType = hostType 
            PathComponents = path.Split([|'/'|], StringSplitOptions.RemoveEmptyEntries); 
        }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module ActorPath = 

    let empty =
        { 
            Transport = None
            Host = None
            MachineAddress = None 
            Port = None
            MachineAddressType = None 
            PathComponents = [||] 
        }

    let ofString (str:string) =
        let buildComponents (comp:string) = 
            if comp.EndsWith(":")
            then  [| Transport(comp.TrimEnd(':')) |]
            else
                let processHost (host:string) = 
                    match host.Split(':') with
                    | [| host; port |] -> [| MachineAddress(host); Port(Int32.Parse(port)) |]
                    | a -> [| MachineAddress(host) |]
                match comp.Split([|'@'|]) with
                | [|a|] when a.Contains(":") -> processHost a
                | [|node; host|] ->
                    let host = processHost host
                    Array.append [|Host node|] host
                | a -> [|a |> PathComponent|]
            
        let buildPath state comp =
            match comp with
            | Transport(trsn) when trsn <> "*" && (not trsn.IsEmpty) -> { state with Transport = (Some trsn) }
            | Host(sys) when sys <> "*" && (not sys.IsEmpty) -> { state with Host = (Some sys)}
            | MachineAddress(host) when host <> "*" && (not host.IsEmpty) -> 
                let hostType = Uri.CheckHostName(host)
                { state with MachineAddress = (Some host); MachineAddressType = (Some hostType) }
            | Port(port) -> { state with Port = (Some port) }
            | PathComponent(path) -> { state with PathComponents = Array.append state.PathComponents path}
            | _ -> state
             
        str.Split([|"/"|], StringSplitOptions.RemoveEmptyEntries)
        |> Array.collect buildComponents
        |> Array.fold buildPath empty

    let ofUri (uri:Uri) =
        if uri.IsAbsoluteUri
        then ActorPath.Create(uri.LocalPath, uri.Scheme, uri.UserInfo, uri.Host, uri.Port, uri.HostNameType)
        else ActorPath.Create(uri.ToString())  
                   
    let components (path:ActorPath) = 
        match path.Host with
        | Some(s) -> s :: (path.PathComponents |> Array.toList)
        | None -> "*" :: (path.PathComponents |> Array.toList)
        |> List.choose (function
            | "*" -> Some TrieKey.Wildcard
            | "/" -> None
            | a -> Some (TrieKey.Key(a.Trim('/')))
        )
    
    let deadLetter = ofString ("/deadletter")

    let internal setHost (system:string) path =
        if system.IsEmpty
        then { path with Host = None }
        else { path with Host = Some system }

    let toNetAddress (path:ActorPath) = 
        match path.MachineAddressType, path.MachineAddress, path.Port with
        | Some(UriHostNameType.IPv4), Some(host), Some(port) -> NetAddress <| new IPEndPoint(IPAddress.Parse(host), port)
        | Some(UriHostNameType.Dns), Some(host), Some(port) -> 
            match Dns.GetHostEntry(host) with
            | null -> failwithf "Unable to resolve host for path %A" path
            | address -> 
                match address.AddressList |> Seq.tryFind (fun a -> a.AddressFamily = AddressFamily.InterNetwork) with
                | Some(ip) -> NetAddress <| new IPEndPoint(ip, port)
                | None -> failwithf "Unable to find ipV4 address for %s" host
        | a -> failwithf "A host name type of %A is not currently supported" a

    let rebase (basePath:ActorPath) (path:ActorPath) =
        if basePath.IsAbsolute
        then
            let basePort = basePath.Port
            { path with
                Transport = basePath.Transport
                MachineAddress = basePath.MachineAddress
                Port = basePort
                MachineAddressType = basePath.MachineAddressType
            }
        else path