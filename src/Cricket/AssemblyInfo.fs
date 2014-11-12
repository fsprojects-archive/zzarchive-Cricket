namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Cricket")>]
[<assembly: AssemblyProductAttribute("Cricket")>]
[<assembly: AssemblyDescriptionAttribute("An actor library for F#.")>]
[<assembly: AssemblyVersionAttribute("0.0.9")>]
[<assembly: AssemblyFileVersionAttribute("0.0.9")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.0.9"
