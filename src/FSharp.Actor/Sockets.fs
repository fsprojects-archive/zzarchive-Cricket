namespace FSharp.Actor

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Net.NetworkInformation
open System.Collections.Concurrent
open System.Threading
open Microsoft.FSharp.Control

module Message = 
    
    let unpack (message : byte[]) = 
        if message.Length > 16
           then
               let messageIdBytes = message.[0..15]
               let ipAddress = IPAddress(message.[16..19])
               let port = BitConverter.ToInt32(message.[20..23], 0)
               let endpoint = new IPEndPoint(ipAddress, port)
               let guid = new Guid(messageIdBytes)
               Some (NetAddress.OfEndPoint endpoint, guid, message.[24..])
        else None

    let pack (endpoint:IPEndPoint) (messageId:Guid) payload =
        let ipEndpoint = endpoint.Address.GetAddressBytes()
        let portBytes = BitConverter.GetBytes endpoint.Port
        let header = Array.append (Array.append (messageId.ToByteArray()) ipEndpoint) portBytes
        Array.append header payload

type Pool<'a>(ctx, poolSize:int, ctor) = 
    let pool = new BlockingCollection<'a>(poolSize)
    let disposables = new ResizeArray<IDisposable>(poolSize)
    let mutable isDisposed = false;

    do
        for i in 0 .. poolSize - 1 do
           let (instance, disposer) = ctor i
           if disposer <> null then disposables.Add(disposer)
           pool.Add(instance) 

    member x.CheckIn(a:'a) =
        pool.Add(a)

    member x.Checkout(?timeout) = 
        if not isDisposed
        then 
            let result = ref Unchecked.defaultof<_>
            match pool.TryTake(result, defaultArg timeout 1000) with
            | true -> pool.Count, !result
            | false -> failwithf "Unable to retrieve from pool (Context: %s)" ctx
        else failwithf "Object disposed (Context: %s)" ctx

    interface IDisposable with
        member x.Dispose() = 
            isDisposed <- true
            for d in disposables do
                d.Dispose()


module Socket = 
        
    type Token = {
        Id : int
        Socket : Socket
        Endpoint : IPEndPoint
    }

    type Event = 
        | Connected of IPEndPoint
        | Received of IPEndPoint * byte[]
        | Sent of IPEndPoint * byte[]
        | Disconnected of IPEndPoint

    type Connection = {
        Checkout : (unit -> SocketAsyncEventArgs)
        Checkin : (SocketAsyncEventArgs -> unit)
        Socket : Socket
        BufferSize : int
        Handler : (Event -> unit) 
    }

    type SocketArgPool(ctx, poolSize, socketBufferSize) =
        let buffer = Array.zeroCreate<byte> (poolSize * socketBufferSize)
        
        let mutable pool = Unchecked.defaultof<Pool<SocketAsyncEventArgs>>        

        member x.BufferSize with get() = socketBufferSize

        member x.CheckIn(args:SocketAsyncEventArgs) = 
            if args.Count < socketBufferSize then args.SetBuffer(args.Offset, socketBufferSize)
           // printfn "Checked in %d" (args.UserToken :?> Token).Id
            args.UserToken <- {(args.UserToken :?> Token) with Socket = null; Endpoint = null }
            pool.CheckIn(args)

        member x.CheckOut() = 
            let (id,args) = pool.Checkout()
            if args.UserToken = null then args.UserToken <- { Id = id; Socket = null; Endpoint = null }
           // printfn "Checked out %d" id
            args

        member x.Start(callback) = 
            let build = 
                (fun i -> 
                    let args = new SocketAsyncEventArgs()
                    args.SetBuffer(buffer, i * socketBufferSize, socketBufferSize)
                    args.Completed |> Event.add callback
                    args, null
                )
            pool <- new Pool<SocketAsyncEventArgs>(ctx, poolSize, build)
          
    module Common = 

        let read (args:SocketAsyncEventArgs) = 
            let data = Array.zeroCreate<byte> args.BytesTransferred
            Buffer.BlockCopy(args.Buffer, args.Offset, data, 0, args.BytesTransferred)
            data

        let disconnect connection (args:SocketAsyncEventArgs) = 
            let token = (args.UserToken :?> Token)
            try
                token.Socket.Shutdown(SocketShutdown.Both)
                if token.Socket.Connected then token.Socket.Disconnect(true)
                connection.Handler (Disconnected token.Endpoint)
            finally 
                token.Socket.Close()

        let executeSafe f g args = if not(f args) then g args

        let create (pool:SocketArgPool) handler (endpoint:IPEndPoint) =
            let socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            socket.Bind(endpoint)
            { Checkout = pool.CheckOut; Checkin = pool.CheckIn; Socket = socket; BufferSize = pool.BufferSize; Handler = handler; }

    module Server = 
        
        open Common

        let rec private accept (connection:Connection) args = 
            let args = if args <> null then args else connection.Checkout()
            args.AcceptSocket <- null;
            executeSafe connection.Socket.AcceptAsync (completeAccept connection) args
        
        and private completeAccept connection (args:SocketAsyncEventArgs) =
            try
                match args.LastOperation with
                | SocketAsyncOperation.Accept -> processAccept connection args 
                | SocketAsyncOperation.Disconnect -> Common.disconnect connection args
                | a -> failwithf "compelteAccept BROKEN unrecognised operation!!! %A" a 
            finally
                args.DisconnectReuseSocket <- true
                connection.Checkin(args)

        and private processAccept connection (args:SocketAsyncEventArgs) = 
            let socket = args.AcceptSocket
            match args.SocketError with
            | SocketError.Success ->
                let readArgs = connection.Checkout()
                readArgs.UserToken <- { (readArgs.UserToken :?> Token) with Socket = socket; Endpoint = socket.RemoteEndPoint :?> IPEndPoint }
                executeSafe socket.ReceiveAsync (processReceive connection) readArgs
                
                if args.SocketError = SocketError.Success && args.BytesTransferred > 0 
                then
                    let data = Common.read args
                    connection.Handler (Received (socket.RemoteEndPoint :?> IPEndPoint  , data))

                executeSafe connection.Socket.AcceptAsync (completeAccept connection) (connection.Checkout())
            | a -> failwithf "Socket Error %A" a
        
        and private complete connection (args:SocketAsyncEventArgs) = 
            try
                match args.LastOperation with
                | SocketAsyncOperation.Accept -> processAccept connection args
                | SocketAsyncOperation.Connect -> processConnect connection args
                | SocketAsyncOperation.Receive -> processReceive connection args
                | SocketAsyncOperation.Send -> processSend connection args
                | SocketAsyncOperation.Disconnect -> Common.disconnect connection args
                | a -> failwithf "complete BROKEN unrecognised operation!!! %A" a 
            finally
                connection.Checkin(args)
        
        and private processConnect connection (args:SocketAsyncEventArgs) =
            match args.SocketError with
            | SocketError.Success -> 
                connection.Handler (Connected (args.RemoteEndPoint :?> IPEndPoint))
                executeSafe args.ConnectSocket.ReceiveAsync (complete connection) (connection.Checkout())
            | a -> failwithf "processConnect BROKEN unrecognised operation!!! %A" a 

        and private processReceive connection (args:SocketAsyncEventArgs) =
            let token = args.UserToken :?> Token
            if args.SocketError = SocketError.Success && args.BytesTransferred > 0 
            then
                let data = Common.read args
                connection.Handler (Received (token.Endpoint , data))

                if token.Socket.Connected then 
                    let newArg = connection.Checkout()
                    newArg.UserToken <- token
                    executeSafe token.Socket.ReceiveAsync (complete connection) newArg
            else Common.disconnect connection args

        and private processSend connection (args:SocketAsyncEventArgs) =
            let token = args.UserToken :?> Token
            match args.SocketError with
            | SocketError.Success -> 
                connection.Handler (Sent (args.RemoteEndPoint :?> IPEndPoint, read args))
                executeSafe token.Socket.ReceiveAsync (complete connection) args
            | a -> Common.disconnect connection args

        let create handler poolSize bufferSize (endpoint:IPEndPoint) =
            let pool = new SocketArgPool("SAEA pool", poolSize, bufferSize)
            let connection = Common.create pool handler endpoint
            pool.Start (complete connection)
            connection

        let listen connection backlog = 
            connection.Socket.Listen(backlog)

            for i in 1 .. backlog do
                accept connection null

    module Client = 
        
        exception ConnectionLost of IPEndPoint

        open Common

        let rec private complete connection (args:SocketAsyncEventArgs) = 
            try
                match args.LastOperation with
                | SocketAsyncOperation.Connect -> processConnect connection args
                | SocketAsyncOperation.Disconnect -> disconnect connection args
                | SocketAsyncOperation.Receive -> processReceive connection args
                | SocketAsyncOperation.Send -> processSend connection args
                | a -> failwithf "complete BROKEN unrecognised operation!!! %A" a 
            finally
                connection.Checkin(args)

        and private processReceive connection (args:SocketAsyncEventArgs) =
            let token = args.UserToken :?> Token
            if args.SocketError = SocketError.Success
            then
                if args.BytesTransferred > 0 
                then
                    let data = Common.read args
                    connection.Handler (Received (token.Endpoint , data))

                    if token.Socket.Connected then 
                        let newArg = connection.Checkout()
                        newArg.UserToken <- token
                        executeSafe token.Socket.ReceiveAsync (complete connection) newArg

        and private processSend connection (args:SocketAsyncEventArgs) =
            let token = args.UserToken :?> Token
            match args.SocketError with
            | SocketError.Success -> 
                connection.Handler (Sent (args.RemoteEndPoint :?> IPEndPoint, read args))
            | a -> Common.disconnect connection args

        and private processConnect connection args = 
            match args.SocketError with
            | SocketError.Success -> 
                connection.Handler (Connected (args.RemoteEndPoint :?> IPEndPoint))
                executeSafe args.ConnectSocket.ReceiveAsync (complete connection) (connection.Checkout())
            | a -> failwithf "processConnect BROKEN unrecognised operation!!! %A" a 
        
        let create handler poolSize bufferSize remoteEndpoint = 
            let pool = new SocketArgPool("SAEA client pool", poolSize, bufferSize)
            let connection = Common.create pool handler (new IPEndPoint(IPAddress.Any, 0))
            pool.Start (complete connection)
            let args = connection.Checkout() 
            args.SetBuffer(args.Offset, 1)
            args.RemoteEndPoint <- remoteEndpoint
            connection.Socket.Connect(remoteEndpoint)
            //executeSafe connection.Socket.ConnectAsync (complete connection) args
            connection

        let send connection (data:byte[]) = 
            let rec send' offset = 
                if offset < data.Length
                then
                    if connection.Socket.Connected
                    then 
                        let saea = connection.Checkout()
                        let size = min (data.Length - offset) connection.BufferSize
                        saea.UserToken <- { (saea.UserToken :?> Token) with Socket = connection.Socket; Endpoint = connection.Socket.RemoteEndPoint :?> IPEndPoint }
                        Buffer.BlockCopy(data, offset, saea.Buffer, saea.Offset, size)
                        saea.SetBuffer(saea.Offset, size)
                        executeSafe connection.Socket.SendAsync (complete connection) saea
                        send' (offset + size)
                    else raise (ConnectionLost(connection.Socket.RemoteEndPoint :?> IPEndPoint))
            send' 0
 
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
    PoolSize : int
    BufferSize : int
}
with
    static member Default<'a>(listenerEndpoint, ?backlog, ?poolSize, ?bufferSize) : TcpConfig = 
        {
            ListenerEndpoint = listenerEndpoint
            Backlog = defaultArg backlog 100
            PoolSize = defaultArg poolSize 300
            BufferSize = defaultArg bufferSize 4096
        }

type TCP(config:TcpConfig) =        
    let mutable isStarted = false
    let mutable handler : (NetAddress * Guid * byte[] -> Async<Unit>) = (fun (_,_,_) -> async { return () })
    let mutable cancellationToken  : CancellationToken = Unchecked.defaultof<_>
    let clients = new ConcurrentDictionary<string, Socket.Connection>()

    let rec messageHandler message = async {
            match Message.unpack message with
            | Some(msg) -> do! handler msg
            | None -> ()
        }

    let onReceive event = 
        match event with
        | Socket.Connected(ep) -> ()//printfn "%A" event
        | Socket.Disconnected(ep) -> ()//printfn "%A" event
        | Socket.Received(address, bytes) -> Async.Start(messageHandler bytes, cancellationToken)
        | Socket.Sent(address, bytes) -> ()//printfn "%A" event

    let socket = Socket.Server.create onReceive config.PoolSize config.BufferSize config.ListenerEndpoint

    let publishAsync (endpoint:IPEndPoint) (messageId:Guid) payload = async {
            let msg = Message.pack config.ListenerEndpoint messageId payload
            let key = endpoint.ToString()
            let client = clients.GetOrAdd(key, fun _ -> Socket.Client.create onReceive config.PoolSize config.BufferSize endpoint) 
            Socket.Client.send client msg        
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
            Socket.Server.listen socket config.Backlog
            isStarted <- true