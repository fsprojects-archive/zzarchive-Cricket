namespace FSharp.Actor

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Net.NetworkInformation
open System.Collections.Concurrent
open System.Threading
open Microsoft.FSharp.Control
 
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
            client.AllowNatTraversal(true)
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

type TCP(config:TcpConfig) =        
    let mutable isStarted = false
    let mutable handler : (NetAddress * Guid * byte[] -> Async<Unit>) = (fun (_,_,_) -> async { return () })
    let mutable listeningEndpoint : IPEndPoint = null
    let clients = new ConcurrentDictionary<string, TcpClient>()

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
            let key = endpoint.ToString()
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