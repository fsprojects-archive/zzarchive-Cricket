namespace Cricket

open System
open System.Net
open System.Net.Sockets
open System.Net.NetworkInformation

[<AutoOpen>]
module internal Helpers = 

    type String with
        member x.IsEmpty 
            with get() = String.IsNullOrEmpty(x) || String.IsNullOrWhiteSpace(x)

    module Option = 
        
        let stringIsNoneIfBlank (str : string option) = 
            str |> Option.bind (fun sys -> if sys.IsEmpty then None else Some sys)

    module Environment = 
        
        let CurrentProcess = Diagnostics.Process.GetCurrentProcess()
        let ProcessName, ProcessId = CurrentProcess.ProcessName, CurrentProcess.Id
        let DefaultActorHostName = sprintf "%s_%s_%d" Environment.MachineName ProcessName ProcessId

module internal Net =

    let getIPAddress() = 
        if NetworkInterface.GetIsNetworkAvailable()
        then 
            let host = Dns.GetHostEntry(Dns.GetHostName())
            host.AddressList
            |> Seq.find (fun add -> add.AddressFamily = AddressFamily.InterNetwork)
        else IPAddress.Loopback
    
    let getFirstFreePort() = 
        let defaultPort = 8080
        let usedports = 
            IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners() 
            |> Seq.map (fun x -> x.Port)
        
        let ports = 
            seq { 
                for port in defaultPort..defaultPort + 2048 do
                    yield port
            }
        
        let port = ports |> Seq.find (fun p -> Seq.forall ((<>) p) usedports)
        port

[<AutoOpen>]
module SystemNetExtensions =      
    type IPEndPoint with
        static member Create(?port:int) = 
            let port = defaultArg port (Net.getFirstFreePort())
            let ipAddress = Net.getIPAddress() 
            new IPEndPoint(ipAddress, port)

type MessageId = Guid

[<CustomComparison; CustomEquality>]
type NetAddress = 
    | NetAddress of IPEndPoint
    override x.Equals(y:obj) =
        match y with
        | :? IPEndPoint as ip -> ip.ToString().Equals(x.ToString())
        | :? NetAddress as add -> 
            match add with
            | NetAddress(null) -> false
            | NetAddress(ip) ->  ip.ToString().Equals(x.ToString())
        | _ -> false
    member x.Endpoint 
        with get() = 
            match x with
            | NetAddress(ip) -> ip
    member x.HostName
        with get() = 
            match Dns.GetHostEntry(x.Endpoint.Address) with
            | null -> failwithf "Unable to get hostname for IPAddress: %A" x.Endpoint.Address
            | he -> he.HostName
    member x.Port
        with get() = x.Endpoint.Port
    override x.GetHashCode() = 
        match x with
        | NetAddress(ip) -> ip.GetHashCode()
    static member OfEndPoint(ip:EndPoint) = NetAddress(ip :?> IPEndPoint)
    interface IComparable with
        member x.CompareTo(y:obj) =
            match y with
            | :? IPEndPoint as ip -> ip.ToString().CompareTo(x.ToString())
            | :? NetAddress as add -> 
                match add with
                | NetAddress(ip) ->  ip.ToString().CompareTo(x.ToString())
            | _ -> -1


        

    