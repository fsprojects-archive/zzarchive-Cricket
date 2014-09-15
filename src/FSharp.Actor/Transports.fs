namespace FSharp.Actor

open System
open System.Threading
open FSharp.Actor

type TCPTransport(config:TcpConfig, ?logger) as self = 
    let scheme = "actor.tcp"
    let basePath = ActorPath.ofString (sprintf "%s://%s/" scheme (config.ListenerEndpoint.ToString()))
    let log = defaultArg logger (Log.defaultFor Log.Debug)
    let logger = new Log.Logger(sprintf "actor.tcp://%A" config.ListenerEndpoint, log)
    let metricContext = Metrics.createContext (sprintf "transports/%s" scheme)
    let receivedRate = Metrics.createMeter metricContext "msgs_received"
    let publishRate = Metrics.createMeter metricContext "msgs_published"
    let sendTimer = Metrics.createTimer metricContext "time_to_send"

    let mutable token = Unchecked.defaultof<CancellationToken>
    let mutable serializer = Unchecked.defaultof<ISerializer>

    let handler =(fun (address:NetAddress, msgId, payload) -> 
                    try
                        receivedRate.Mark(1L)
                        let msg = serializer.Deserialize<RemoteMessage>(payload)
                        (!~msg.Target).Post(msg.Message, new RemoteActor(msg.Sender, self) :> IActor |> ActorRef)
                    with e -> 
                        logger.Error("Error handling message: " + e.Message, exn = e)
                 )

    let tcp = new TCP(config)

    let post target payload = 
        async {
            publishRate.Mark(1L)
            do sendTimer(fun () -> Async.StartImmediate(tcp.PublishAsync((ActorPath.toNetAddress target).Endpoint, serializer.Serialize payload), token))
        }

    interface ITransport with
        member x.Scheme with get() = scheme
        member x.BasePath with get() = basePath
        member x.Post(target, payload) = Async.Start(post target payload)
        member x.Start(serialiser, ct) = 
            serializer <- serialiser
            ct.Register(fun () -> (x :> IDisposable).Dispose()) |> ignore
            tcp.Start(handler, ct)
        member x.Dispose() = 
            (tcp :> IDisposable).Dispose()

type TcpActorRegistryTransport(config:TcpConfig) = 
    let tcpChannel = new TCP(config)
    let logger = Log.Logger("TcpActorRegistryTransport", Log.defaultFor Log.Debug)
    let mutable settings : ActorRegistryTransportSettings = Unchecked.defaultof<_>
    let metricContext = Metrics.createContext "transports/TcpActorRegistryTransport"
    let receivedRate = Metrics.createMeter metricContext "msgs_received"
    let publishRate = Metrics.createMeter metricContext "msgs_published"
    let sendTimer = Metrics.createTimer metricContext "time_to_send"

    let handler internalHandler = 
        (fun (address,msgId, payload) ->
                try
                    receivedRate.Mark(1L)
                    let msg = settings.Serializer.Deserialize payload
                    internalHandler (address,msg,msgId)
                with e -> 
                    logger.Error("Unable to handle Registry Transport message", exn = e)
        )

    interface IActorRegistryTransport with
        member x.ListeningEndpoint with get() = NetAddress config.ListenerEndpoint
        member x.Post(NetAddress endpoint, msg, msgId) = 
            publishRate.Mark(1L)
            sendTimer (fun () -> tcpChannel.Publish(endpoint, settings.Serializer.Serialize msg, msgId))
        member x.Start(setts) = 
            settings <- setts
            settings.CancellationToken.Register(fun () -> (x :> IActorRegistryTransport).Dispose()) |> ignore
            tcpChannel.Start(handler settings.TransportHandler, settings.CancellationToken)
        member x.Dispose() = 
            (tcpChannel :> IDisposable).Dispose()

type UdpActorRegistryDiscovery(udpConfig:UdpConfig, ?broadcastInterval) = 
    let udpChannel = new UDP(udpConfig)
    let logger = Log.Logger("UdpActorRegistryDiscovery", Log.defaultFor Log.Debug)
    let broadcastInterval = defaultArg broadcastInterval 1000
    let mutable settings : ActorRegistryDiscoverySettings = Unchecked.defaultof<_>
    let metricContext = Metrics.createContext "transports/UdpRegistryDiscovery"
    let receivedRate = Metrics.createMeter metricContext "msgs_received"

    let handler internalHandler = 
        (fun (_, payload) ->
                try
                    receivedRate.Mark(1L)
                    let msg = settings.Serializer.Deserialize<ActorDiscoveryBeacon>(payload)
                    internalHandler msg
                with e ->
                    logger.Error("Unable to handle discovery msg.", exn = e)
        )

    interface IActorRegistryDiscovery with
        member x.Start(setts) =
            settings <- setts
            settings.CancellationToken.Register(fun () -> (x :> IActorRegistryDiscovery).Dispose()) |> ignore
            let beaconBytes = settings.Serializer.Serialize(ActorSystemDiscoveryBeacon(settings.SystemDetails))
            udpChannel.Start(handler settings.DiscoveryHandler, settings.CancellationToken)
            udpChannel.Heartbeat(broadcastInterval, (fun _ -> beaconBytes))

        member x.Dispose() = 
            udpChannel.Publish(settings.Serializer.Serialize(ActorSystemShutdownBeacon(settings.SystemDetails))) |> ignore
            (udpChannel :> System.IDisposable).Dispose()