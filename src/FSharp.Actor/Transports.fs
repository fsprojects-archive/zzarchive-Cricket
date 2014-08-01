namespace FSharp.Actor

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Collections.Concurrent
open FSharp.Actor

type TCPTransport(config:TcpConfig, ?logger) as self = 
    let scheme = "actor.tcp"
    let basePath = ActorPath.ofString (sprintf "%s://%s/" scheme (config.ListenerEndpoint.ToString()))
    let log = defaultArg logger (Log.defaultFor Log.Debug)
    let logger = new Log.Logger(sprintf "actor.tcp://%A" config.ListenerEndpoint, log)
    let mutable serializer = Unchecked.defaultof<ISerializer>

    let handler =(fun (address:NetAddress, msgId, payload) -> 
                  async {
                    try
                        let msg = serializer.Deserialize<RemoteMessage>(payload)
                        (!~msg.Target).Post(msg.Message, new RemoteActor(msg.Sender, self) :> IActor |> ActorRef)
                    with e -> 
                        logger.Error("Error handling message: " + e.Message, exn = e)
                  })

    let tcp = new TCP(config) 

    interface ITransport with
        member x.Scheme with get() = scheme
        member x.BasePath with get() = basePath
        member x.Post(target, payload) = Async.Start(tcp.PublishAsync((ActorPath.toNetAddress target).Endpoint, serializer.Serialize payload))
        member x.Start(serialiser, ct) = 
            serializer <- serialiser
            tcp.Start(handler, ct)

type TcpActorRegistryTransport(config:TcpConfig) = 
    let tcpChannel = new TCP(config)
    let logger = Log.Logger("TcpActorRegistryTransport", Log.defaultFor Log.Debug)
    let mutable settings : ActorRegistryTransportSettings = Unchecked.defaultof<_>

    let handler internalHandler = 
        (fun (address,msgId, payload) ->
             async {
                try
                    let msg = settings.Serializer.Deserialize payload
                    do! internalHandler (address,msg,msgId)
                with e -> 
                    logger.Error("Unable to handle Registry Transport message", exn = e) 
             }
        )

    interface IActorRegistryTransport with
        member x.ListeningEndpoint with get() = NetAddress config.ListenerEndpoint
        member x.Post(NetAddress endpoint, msg, msgId) = 
            tcpChannel.PublishAsync(endpoint, settings.Serializer.Serialize msg, msgId)
        member x.Start(setts) = 
            settings <- setts
            tcpChannel.Start(handler settings.TransportHandler, settings.CancellationToken)

type UdpActorRegistryDiscovery(udpConfig:UdpConfig, ?broadcastInterval) = 
    let udpChannel = new UDP(udpConfig)
    let logger = Log.Logger("UdpActorRegistryDiscovery", Log.defaultFor Log.Debug)
    let broadcastInterval = defaultArg broadcastInterval 1000
    let mutable settings : ActorRegistryDiscoverySettings = Unchecked.defaultof<_>

    let handler internalHandler = 
        (fun (_, payload) ->
            async {
                try
                    let msg = settings.Serializer.Deserialize<ActorDiscoveryBeacon>(payload)
                    do! internalHandler msg
                with e ->
                    logger.Error("Unable to handle discovery msg.", exn = e)
            }
        )

    interface IActorRegistryDiscovery with
        member x.Start(setts) =
            settings <- setts
            let beaconBytes = settings.Serializer.Serialize(settings.Beacon)
            udpChannel.Start(handler settings.DiscoveryHandler, settings.CancellationToken)
            udpChannel.Heartbeat(broadcastInterval, (fun _ -> beaconBytes), settings.CancellationToken)