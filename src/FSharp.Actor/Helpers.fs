namespace FSharp.Actor

open System

[<AutoOpen>]
module internal Helpers = 

    type String with
        member x.IsEmpty 
            with get() = String.IsNullOrEmpty(x) || String.IsNullOrWhiteSpace(x)

    module Option = 
        
        let stringIsNoneIfBlank (str : string option) = 
            str |> Option.bind (fun sys -> if sys.IsEmpty then None else Some sys)

        

    