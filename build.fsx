// include Fake lib
#I "packages/FAKE/tools/"

#r "FakeLib.dll"

open Fake 
open Razhel.Configuration

#load "razhel.fsx"

let title = "Razhel" 
let description = "Static website generator"
let config = 
    { 
        SourceDir = __SOURCE_DIRECTORY__ </> "src" 
        OutputDir = __SOURCE_DIRECTORY__ </> "docs"
        BaseUrl = "/"
        CopyDirs = ["img";"css";"js"]
    }

// Targets
Target "?" (fun _ ->
    printfn " *********************************************************"
    printfn " *        Available options (call 'build <Target>')      *"
    printfn " *********************************************************"
    printfn " [Build]"
    printfn "  > Clean"
    printfn "  > Generate"
    printfn "  > Run"
    printfn " "
    printfn " [Help]"
    printfn "  > ?"
    printfn " "
    printfn " *********************************************************"
)


let setBaseUrl config =
    match getBuildParam "baseUrl" with
    | "" | null -> config
    | url -> { config with BaseUrl = url }

Target "Clean" (fun _ -> Razhel.clean config |> ignore)
Target "Generate" (fun _ -> config |> setBaseUrl |> Razhel.generate |> ignore)
Target "Run" (fun _ -> config |> setBaseUrl |> Razhel.run)

// start build
RunTargetOrDefault "?"