namespace FSharp.Actor
    
    open System.Net

    type SupervisorMessage = 
        | Errored of ActorRef * exn
        | Terminated of ActorRef

    type SystemMessage = 
        | Supervisor of SupervisorMessage
        | Restart
        | Die

    type RemotingError = 
        | CouldNotPost of ActorPath.T
    
    type RemoteMessage =
        | Post of ActorPath.T * obj
        | Received of byte[]
        | Connected of IPEndPoint
        | Disconnected of IPEndPoint
        | Sent of int * IPEndPoint
        | Error of RemotingError * exn option

