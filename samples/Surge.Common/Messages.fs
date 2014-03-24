namespace Surge.Common.Messages

type RequestMessage =
    | Connect
    | Subscribe of string
    | Unsubscribe of string

type RequestWithPath = {
    Request: RequestMessage
    ResponsePath: string
}

//type Subscribe = {
//     
//}
//
//type Quote()