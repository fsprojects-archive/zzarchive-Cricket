// --------------------------------------------------------------------------------------
// Builds the documentation from FSX files in the 'samples' directory
// (the documentation is stored in the 'docs' directory)
// --------------------------------------------------------------------------------------

#I "packages/FSharp.Formatting.1.0.15/lib/net40"
#load "packages/FSharp.Formatting.1.0.15/literate/literate.fsx"
open System.IO
open FSharp.Literate

let template = Path.Combine(__SOURCE_DIRECTORY__, "template.html")
let sources = Path.Combine(__SOURCE_DIRECTORY__, "samples")
let output = Path.Combine(__SOURCE_DIRECTORY__, "../fsharp.actor.pages/docs/")

if Directory.Exists(output)
then Directory.Delete(output, true)

Literate.ProcessDirectory(sources, template, output)
