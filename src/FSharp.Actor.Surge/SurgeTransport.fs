namespace FSharp.Actor.Surge

open System
open FSharp.Actor
open System.Threading
open System.Collections.Concurrent
open FSharp.Actor
open FSharp.Actor.Types
open FSharp.Actor.Remoting

[<AutoOpen>]
module TcpExtensions =
    type System.Net.Sockets.TcpListener with
        member x.AsyncAcceptTcpClient() =
            Async.FromBeginEnd(x.BeginAcceptTcpClient, x.EndAcceptTcpClient)

type SurgeMessage = {
    Sender : ActorPath option
    Target : ActorPath
    Body : obj
}

