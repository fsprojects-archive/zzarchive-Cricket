namespace FSharp.Actor

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Net.NetworkInformation
open System.Collections.Concurrent
open System.Threading
open Microsoft.FSharp.Control
open Nessos.FsPickler

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
    
    let availableEndpoint() =
        new IPEndPoint(getIPAddress(), getFirstFreePort())

    let sendTcpAsync endpoint msg = async {
            use client = new TcpClient()
            client.Connect(endpoint)
            use stream  = client.GetStream()
            do! stream.WriteBytesAsync(msg)
            do! stream.FlushAsync().ContinueWith(ignore) |> Async.AwaitTask
        }
  
[<AutoOpen>]
module SystemNetExtensions =      
    type IPEndPoint with
        static member Create(?port:int) = new IPEndPoint(Net.getIPAddress(), defaultArg port (Net.getFirstFreePort()))

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


type UdpConfig = {
    Id : Guid
    MulticastPort : int
    MulticastGroup : IPAddress
}
with
    static member Default<'a>(?id, ?port, ?group) = 
        {
            Id = defaultArg id (Guid.NewGuid())
            MulticastPort = defaultArg port 2222
            MulticastGroup = defaultArg group (IPAddress.Parse("239.0.0.222"))
        }
    member x.RemoteEndpoint = new IPEndPoint(x.MulticastGroup, x.MulticastPort)

type UDP(config:UdpConfig) =       
    let mutable isStarted = false
    let mutable handler = (fun (_,_) -> async { return () })

    let publisher =
        lazy
            let client = new UdpClient()
            client.JoinMulticastGroup(config.MulticastGroup)
            client

    let listener =
        lazy
            let client = new UdpClient()
            client.ExclusiveAddressUse <- false
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true)
            client.Client.Bind(new IPEndPoint(IPAddress.Any, config.MulticastPort))
            client.JoinMulticastGroup(config.MulticastGroup)
            client

    let rec messageHandler() = async {
            let! received = listener.Value.ReceiveAsync() |> Async.AwaitTask
            if received.Buffer.Length > 16 
            then 
                let guid = new Guid(received.Buffer.[0..15])
                if guid <> config.Id
                then 
                    do! handler (NetAddress.OfEndPoint received.RemoteEndPoint, received.Buffer.[16..])
                
            return! messageHandler()
        }

    let publish payload = async {
            let bytes = Array.append (config.Id.ToByteArray()) payload
            let! bytesSent = publisher.Value.SendAsync(bytes, bytes.Length, config.RemoteEndpoint) |> Async.AwaitTask
            return bytesSent
        }

    let rec heartBeat interval payloadF = async {
        do! publish (payloadF()) |> Async.Ignore
        do! Async.Sleep(interval)
        return! heartBeat interval payloadF
    }
    
    member x.Publish payload = 
        publish payload |> Async.RunSynchronously
    
    member x.Heartbeat(interval, payloadF, ?ct) =
        Async.Start(heartBeat interval payloadF, ?cancellationToken = ct)
       
    member x.Start(msghandler,ct) = 
        if not(isStarted)
        then 
            handler <- msghandler
            Async.Start(messageHandler(), ct)
            isStarted <- true

type TcpConfig = {
    ListenerEndpoint : IPEndPoint
    Backlog : int
}
with
    static member Default<'a>(listenerEndpoint, ?backlog) : TcpConfig = 
        {
            ListenerEndpoint = listenerEndpoint
            Backlog = defaultArg backlog 10000
        }

type TcpMessageId = Guid

type TcpHandlerResponse = 
    | Payload of byte[]
    | Empty

type TCP(config:TcpConfig) =        
    let mutable isStarted = false
    let mutable handler : (NetAddress * Guid * byte[] -> Async<Unit>) = (fun (_,_,_) -> async { return () })
    let mutable listeningEndpoint : IPEndPoint = null

    let listener =
        lazy
            let l = new TcpListener(config.ListenerEndpoint)
            l.Start(config.Backlog)
            listeningEndpoint <- config.ListenerEndpoint
            l

    let rec messageHandler (listener:TcpListener) = async {
            let! client = listener.AcceptTcpClientAsync() |> Async.AwaitTask
            use client = client
            let stream = client.GetStream()
            let! (message:byte[]) = stream.ReadBytesAsync()
            if message.Length > 16
            then
                let messageIdBytes = message.[0..15]
                let ipAddress = IPAddress(message.[16..19])
                let port = BitConverter.ToInt32(message.[20..23], 0)
                let endpoint = new IPEndPoint(ipAddress, port)
                let guid = new Guid(messageIdBytes)
                do! handler (NetAddress.OfEndPoint endpoint, guid, message.[24..])
            return! messageHandler listener
        }

    let publishAsync (endpoint:IPEndPoint) (messageId:Guid) payload = async {
            let ipEndpoint = listeningEndpoint.Address.GetAddressBytes()
            let portBytes = BitConverter.GetBytes listeningEndpoint.Port
            let header = Array.append (Array.append (messageId.ToByteArray()) ipEndpoint) portBytes
            use client = new TcpClient()           
            client.Connect(endpoint)
            use stream  = client.GetStream()
            do! stream.WriteBytesAsync(Array.append header payload)
            do! stream.FlushAsync().ContinueWith(ignore) |> Async.AwaitTask
        }
    
    member x.Publish(endpoint, payload, ?messageId) = 
        x.PublishAsync(endpoint, payload, ?messageId = messageId) |> Async.RunSynchronously

    member x.PublishAsync(endpoint, payload, ?messageId) = 
        publishAsync endpoint (defaultArg messageId (Guid.NewGuid())) payload
    
    member x.Endpoint = config.ListenerEndpoint

    member x.Start(msgHandler, ct) =
        if not isStarted
        then 
            handler <- msgHandler
            Async.Start(messageHandler listener.Value, ct)
            isStarted <- true