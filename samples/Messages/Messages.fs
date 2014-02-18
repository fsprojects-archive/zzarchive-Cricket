namespace Messages

open System.Net
open FSharp.Actor

    type ConnectionMessage =
        | Connect of username : string * clientPath : string
        | Disconnect of username : string

    type ClientBoundMessage =
        | TextMessage of message : string

