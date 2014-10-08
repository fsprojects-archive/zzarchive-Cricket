namespace FSharp.Actor

open System
open System.Net
open System.Net.Sockets
open System.Collections.Concurrent
open System.Threading
open Microsoft.FSharp.Control

module Message = 
    
    let unpack (message : byte[]) =
        try
           let guid = new Guid(message.[0..15])
           let ipAddress = IPAddress(message.[16..19])
           let port = BitConverter.ToInt32(message.[20..23], 0)
           let endpoint = new IPEndPoint(ipAddress, port)
           NetAddress.OfEndPoint endpoint, guid, message.[24..]
        with e -> 
            raise (InvalidMessageException(message, e))

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
    
    exception ConnectionLost of IPEndPoint
       
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
            args.UserToken <- {(args.UserToken :?> Token) with Socket = null; Endpoint = null }
            pool.CheckIn(args)

        member x.CheckOut() = 
            let (id,args) = pool.Checkout()
            if args.UserToken = null then args.UserToken <- { Id = id; Socket = null; Endpoint = null }
            args

        member x.Start(callback) = 
            let build = 
                (fun i -> 
                    let args = new SocketAsyncEventArgs()
                    args.SetBuffer(buffer, i * socketBufferSize, socketBufferSize)
                    let dispose = args.Completed |> Observable.subscribe callback
                    args, dispose
                )
            pool <- new Pool<SocketAsyncEventArgs>(ctx, poolSize, build)

    let read (args:SocketAsyncEventArgs) = 
        let length = BitConverter.ToInt32(args.Buffer, args.Offset)
        let data = Array.zeroCreate<byte> length
        Buffer.BlockCopy(args.Buffer, args.Offset + 4, data, 0, length)
        data
    
    let executeSafe f g args = if not(f args) then g args
    
    let create (pool:SocketArgPool) handler (endpoint:IPEndPoint) =
        let socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        socket.Bind(endpoint)
        { Checkout = pool.CheckOut; Checkin = pool.CheckIn; Socket = socket; BufferSize = pool.BufferSize; Handler = handler; }
    
    type Server private(endpoint:IPEndPoint, handler, ?backlog, ?poolSize, ?bufferSize) = 
        let backlog = defaultArg backlog 1000
        let poolSize = defaultArg poolSize 1000
        let bufferSize = defaultArg bufferSize 4096

        let connectionPool = new SocketArgPool("connection pool", max poolSize (backlog * 2), bufferSize)
        let workerPool = new SocketArgPool("worker pool", poolSize, bufferSize)
        let socket = 
            let socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            socket

        let rec accept args = 
            let args = if args <> null then args else connectionPool.CheckOut()
            args.AcceptSocket <- null;
            executeSafe socket.AcceptAsync complete args
        
        and processAccept (args:SocketAsyncEventArgs) = 
            let acceptSocket = args.AcceptSocket
            match args.SocketError with
            | SocketError.Success ->                
                if args.SocketError = SocketError.Success && args.BytesTransferred > 0 
                then
                    let data = read args
                    if data.Length > 0
                    then handler (Received (acceptSocket.RemoteEndPoint :?> IPEndPoint  , data))

                let acceptor = connectionPool.CheckOut()
                executeSafe socket.AcceptAsync complete acceptor

                let receiver = workerPool.CheckOut()
                receiver.UserToken <- { (receiver.UserToken :?> Token) with Socket = acceptSocket; Endpoint = acceptSocket.RemoteEndPoint :?> IPEndPoint }
                executeSafe acceptSocket.ReceiveAsync processReceive receiver

            | a -> failwithf "Socket Error %A" a
        
        and complete (args:SocketAsyncEventArgs) = 
            try
                match args.LastOperation with
                | SocketAsyncOperation.Accept -> processAccept args
                | SocketAsyncOperation.Connect -> processConnect args
                | SocketAsyncOperation.Receive -> processReceive args
                | SocketAsyncOperation.Send -> processSend args
                | SocketAsyncOperation.Disconnect -> disconnect args
                | a -> failwithf "complete BROKEN unrecognised operation!!! %A" a 
            finally
                workerPool.CheckIn(args)
        
        and processConnect (args:SocketAsyncEventArgs) =
            match args.SocketError with
            | SocketError.Success -> 
                handler (Connected (args.RemoteEndPoint :?> IPEndPoint))
                executeSafe args.ConnectSocket.ReceiveAsync complete (workerPool.CheckOut())
            | a -> failwithf "processConnect BROKEN unrecognised operation!!! %A" a 

        and processReceive (args:SocketAsyncEventArgs) =
            let token = args.UserToken :?> Token
            if args.SocketError = SocketError.Success && args.BytesTransferred > 0 
            then
                
                let data = read args
                if data.Length > 0
                then handler (Received (token.Endpoint , data))

                if token.Socket.Connected then 
                    let newArg = workerPool.CheckOut()
                    newArg.UserToken <- token
                    executeSafe token.Socket.ReceiveAsync complete newArg
            else disconnect args

        and processSend (args:SocketAsyncEventArgs) =
            let token = args.UserToken :?> Token
            match args.SocketError with
            | SocketError.Success -> 
                handler (Sent (args.RemoteEndPoint :?> IPEndPoint, read args))
                executeSafe token.Socket.ReceiveAsync complete args
            | a -> disconnect args

        and disconnect (args:SocketAsyncEventArgs) = 
            let token = (args.UserToken :?> Token)
            try
                token.Socket.Shutdown(SocketShutdown.Both)
                if token.Socket.Connected then token.Socket.Disconnect(true)
                handler (Disconnected token.Endpoint)
            finally 
                token.Socket.Close()
        
        member x.Listen() =
            socket.Bind(endpoint)
            socket.Listen(backlog)

            workerPool.Start(complete)
            connectionPool.Start(complete)

            for i in 1 .. backlog do
                accept null
        
        interface IDisposable with
            member x.Dispose() = socket.Disconnect(true); socket.Dispose()

        static member Create(endpoint:IPEndPoint, handler, ?backlog, ?poolSize, ?bufferSize) =
            let pool = new Server(endpoint, handler, ?backlog = backlog, ?poolSize = poolSize, ?bufferSize = bufferSize)
            pool

    type Client(handler, ?endpoint:IPEndPoint, ?poolSize, ?bufferSize) = 
        let poolSize = defaultArg poolSize 50
        let bufferSize = defaultArg bufferSize 4096
        let endpoint = defaultArg endpoint (new IPEndPoint(IPAddress.Any, 0))
        let pool = new SocketArgPool("SAEA client pool", poolSize, bufferSize)
        let socket = 
            let socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            socket

        let rec complete (args:SocketAsyncEventArgs) = 
            try
                match args.LastOperation with
                | SocketAsyncOperation.Connect -> processConnect args
                | SocketAsyncOperation.Disconnect -> disconnect args
                | SocketAsyncOperation.Receive -> processReceive args
                | SocketAsyncOperation.Send -> processSend args
                | a -> failwithf "complete BROKEN unrecognised operation!!! %A" a 
            finally
                pool.CheckIn(args)

        and processReceive (args:SocketAsyncEventArgs) =
            let token = args.UserToken :?> Token
            if args.SocketError = SocketError.Success
            then
                if args.BytesTransferred > 0 
                then
                    let data = read args
                    if data.Length > 0
                    then handler (Received (token.Endpoint , data))

                    if token.Socket.Connected then 
                        let newArg = pool.CheckOut()
                        newArg.UserToken <- token
                        executeSafe token.Socket.ReceiveAsync complete newArg

        and processSend (args:SocketAsyncEventArgs) =
            match args.SocketError with
            | SocketError.Success ->
                handler (Sent (args.RemoteEndPoint :?> IPEndPoint, read args))
            | a -> disconnect args

        and processConnect args = 
            match args.SocketError with
            | SocketError.Success -> 
                handler (Connected (args.RemoteEndPoint :?> IPEndPoint))
                executeSafe args.ConnectSocket.ReceiveAsync complete (pool.CheckOut())
            | a -> failwithf "processConnect BROKEN unrecognised operation!!! %A" a

        and disconnect (args:SocketAsyncEventArgs) = 
            let token = (args.UserToken :?> Token)
            try
                token.Socket.Shutdown(SocketShutdown.Both)
                if token.Socket.Connected then token.Socket.Disconnect(true)
                handler (Disconnected token.Endpoint)
            finally 
                token.Socket.Close()
                pool.CheckIn(args)
        
        static member Create(handler, ?endpoint, ?poolSize, ?bufferSize) = 
            let client = new Client(handler, ?endpoint = endpoint, ?poolSize = poolSize, ?bufferSize = bufferSize)
            client

        member x.Connect(remoteEndpoint) =
            pool.Start complete
            let args = pool.CheckOut() 
            args.SetBuffer(args.Offset, 1)
            args.RemoteEndPoint <- remoteEndpoint
            socket.Connect(remoteEndpoint)
            
        member x.Send (data:byte[]) = 
            let data = Array.append (BitConverter.GetBytes(data.Length)) data
            let rec send' offset = 
                if offset < data.Length
                then
                    if socket.Connected
                    then 
                        let saea = pool.CheckOut()
                        let size = min (data.Length - offset) bufferSize
                        saea.UserToken <- { (saea.UserToken :?> Token) with Socket = socket; Endpoint = socket.RemoteEndPoint :?> IPEndPoint }
                        Buffer.BlockCopy(data, offset, saea.Buffer, saea.Offset, size)
                        executeSafe socket.SendAsync complete  saea
                        send' (offset + size)
                    else raise (ConnectionLost(socket.RemoteEndPoint :?> IPEndPoint))
            send' 0

type UdpConnectMethod = 
    | Multicast of group:IPAddress * ttl:int
    | Broadcast of ip:IPAddress
    | Direct of ip:IPAddress

type UdpConfig = {
    Id : Guid
    Port : int
    ConnectMethod : UdpConnectMethod
}
with
    static member Default(?id, ?port, ?connectMethod) = 
        {
            Id = defaultArg id (Guid.NewGuid())
            Port = defaultArg port 15000
            ConnectMethod = defaultArg connectMethod (Multicast(IPAddress.Parse("239.192.0.0"), 2))
        }
    static member Multicast(?id, ?port, ?group) =
        UdpConfig.Default(?id = id, ?port = port, connectMethod = Multicast(defaultArg group (IPAddress.Parse("239.192.0.0")), 2))
    static member Broadcast(?id, ?port, ?address) =
        UdpConfig.Default(?id = id, ?port = port, connectMethod = Broadcast(defaultArg address IPAddress.Broadcast))
    static member Direct(address, ?id, ?port) =
        UdpConfig.Default(?id = id, ?port = port, connectMethod = Direct(address))
    member x.RemoteEndpoint = 
        match x.ConnectMethod with
        | Multicast(group, _) -> new IPEndPoint(group, x.Port)
        | Broadcast(broadcastAddress) -> new IPEndPoint(broadcastAddress, x.Port)
        | Direct(address) -> new IPEndPoint(address, x.Port)


type UDP(config:UdpConfig) =       
    let mutable isStarted = false
    let mutable handler = (fun (_,_) -> ())
    let mutable cancellationToken : CancellationToken = Unchecked.defaultof<_>

    let publisher =
        lazy
            let client = new UdpClient()
            match config.ConnectMethod with
            | Multicast(grp, ttl) ->  
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(grp, IPAddress.Any))
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, ttl)
            | Broadcast _ -> 
                client.EnableBroadcast <- true
            | Direct _ -> ()
            client

    let listener =
        lazy
            let client = new UdpClient()
            client.ExclusiveAddressUse <- false
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true) 
            client.Client.Bind(new IPEndPoint(IPAddress.Any, config.Port))
            match config.ConnectMethod with
            | Multicast(grp, _) -> 
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(grp, IPAddress.Any))
            | Broadcast _ -> 
                client.EnableBroadcast <- true
            | Direct _ -> ()
            client

    let rec messageHandler() = async {
            let! received = listener.Value.ReceiveAsync() |> Async.AwaitTask
            if received.Buffer.Length > 16 
            then 
                let guid = new Guid(received.Buffer.[0..15])
                if guid <> config.Id
                then 
                    handler (NetAddress.OfEndPoint received.RemoteEndPoint, received.Buffer.[16..])
                
            return! messageHandler()
        }

    let publish payload = async {
            if isStarted
            then
                let bytes = Array.append (config.Id.ToByteArray()) payload
                let! bytesSent = publisher.Value.SendAsync(bytes, bytes.Length, config.RemoteEndpoint) |> Async.AwaitTask
                return bytesSent
            else return 0
        }

    let rec heartBeat interval payloadF = async {
        do! publish (payloadF()) |> Async.Ignore
        do! Async.Sleep(interval)
        if isStarted && (not cancellationToken.IsCancellationRequested)
        then return! heartBeat interval payloadF
        else ()
    }
    
    member x.Publish payload = 
        publish payload |> Async.RunSynchronously
    
    member x.Heartbeat(interval, payloadF) =
        Async.Start(heartBeat interval payloadF, cancellationToken)
       
    member x.Start(msghandler,ct:CancellationToken) = 
        if not(isStarted)
        then
            cancellationToken <- ct
            cancellationToken.Register(fun () -> (x :> IDisposable).Dispose()) |> ignore 
            handler <- msghandler
            Async.Start(messageHandler(), cancellationToken)
            isStarted <- true

    interface IDisposable with
        member x.Dispose() = 
            isStarted <- false
            listener.Value.Close()
            publisher.Value.Close()

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
    let mutable handler : (NetAddress * Guid * byte[] -> Unit) = (fun (_,_,_) -> ())
    let mutable cancellationToken  : CancellationToken = Unchecked.defaultof<_>
    let clients = new ConcurrentDictionary<string, Socket.Client>()

    let rec messageHandler recievedFrom message = handler (Message.unpack message)

    let onReceive event =
        match event with
        | Socket.Connected(ep) -> ()//printfn "%A" event
        | Socket.Disconnected(ep) -> ()//printfn "%A" event
        | Socket.Received(address, bytes) -> messageHandler address bytes
        | Socket.Sent(address, bytes) -> ()//printfn "%A" event

    let server = Socket.Server.Create(config.ListenerEndpoint, onReceive, config.Backlog, config.PoolSize, config.BufferSize)

    let publishAsync (endpoint:IPEndPoint) (messageId:Guid) payload = async {
            let msg = Message.pack config.ListenerEndpoint messageId payload
            let key = endpoint.ToString()
            let client = clients.GetOrAdd(key, fun _ -> 
                                                    let c = Socket.Client.Create(onReceive, poolSize = config.PoolSize, bufferSize = config.BufferSize)
                                                    c.Connect(endpoint)
                                                    c
                                         ) 
            client.Send msg        
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
            server.Listen()
            isStarted <- true

    interface IDisposable with
        member x.Dispose() =
            isStarted <- false