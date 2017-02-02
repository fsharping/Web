// include Fake lib
#I "packages/FAKE/tools/"
#I "packages/Fue/lib/net45/"
#I "packages/HtmlAgilityPack/lib/net45/"
#I "packages/Razhel/lib/net45/"
#I "packages/Suave/lib/net40/"
#I "packages/FSharp.Formatting/lib/net40/"

#r "FakeLib.dll"
#r "Fue.dll"
#r "Razhel.dll"
#r "Suave.dll"

open System
open System.IO
open Fake 
open Fake.AssemblyInfoFile
open System.Diagnostics
open Fake.Testing

open Suave
open Suave.Web
open Suave.Http
open Suave.Operators
open Suave.Sockets
open Suave.Sockets.Control
open Suave.Sockets.AsyncSocket
open Suave.WebSocket
open Suave.Utils
open Suave.Files

let title = "Razhel" 
let description = "Static website generator"
let config = { SourceDir = __SOURCE_DIRECTORY__ </> "src"; OutputDir = __SOURCE_DIRECTORY__ </> "docs"} : Razhel.Configuration.Config

// Targets
Target "?" (fun _ ->
    printfn " *********************************************************"
    printfn " *        Available options (call 'build <Target>')      *"
    printfn " *********************************************************"
    printfn " [Build]"
    printfn "  > Generate"
    printfn "  > Run"
    printfn " "
    printfn " [Help]"
    printfn "  > ?"
    printfn " "
    printfn " *********************************************************"
)

let clean() = CleanDir config.OutputDir

Target "Clean" (fun _ ->
    clean()
)

let copyDirs() =
    ["css";"js";"img"]
    |> List.iter (fun dir ->
        let trgt = config.OutputDir </> dir 
        let src = config.SourceDir </> dir 
        CopyDir trgt src (fun _ -> true)
    )
    
Target "CopyDirs" (fun _ ->
    copyDirs()
)

let generate() = 
    clean()
    copyDirs()
    traceImportant <| sprintf "Generating output"
    Razhel.Generator.generateAllPages config

let script = """
    <script language="javascript" type="text/javascript">
        function init()
        {
            websocket = new WebSocket("ws://"+window.location.host+"/websocket");
            websocket.onmessage = function(evt) { location.reload(); };
        }
        window.addEventListener("load", init, false);
    </script>
    """

let addWatcher() =
    let injectWatcher file = File.AppendAllText(file, script)
    Razhel.Pages.getAllPages config 
    |> List.map (fun page -> config.OutputDir </>  Razhel.Pages.getOutputPath(page))
    |> List.filter (fun (x:string) -> x.EndsWith(".html"))
    |> List.iter injectWatcher

let generateWithSockets() =
    generate()
    addWatcher()

Target "Generate" (fun _ ->
    generate()
)

let refreshEvent = new Event<_>()

let handleWatcherEvents (events:FileChange seq) =
    for e in events do
        let fi = fileInfo e.FullPath
        traceImportant <| sprintf "%s was changed." fi.Name
        match fi.Attributes.HasFlag FileAttributes.Hidden || fi.Attributes.HasFlag FileAttributes.Directory with
        | true -> ()
        | _ -> generateWithSockets()
    refreshEvent.Trigger()

let socketHandler (webSocket : WebSocket) =
  fun cx -> socket {
    while true do
      let! refreshed =
        Control.Async.AwaitEvent(refreshEvent.Publish)
        |> Suave.Sockets.SocketOp.ofAsync 
      do! webSocket.send Text (ByteSegment((ASCII.bytes "refreshed"))) true
  }

let startWebServer () =
    let rec findPort port =
        let portIsTaken =
            if isMono then false else
            System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            |> Seq.exists (fun x -> x.Port = port)

        if portIsTaken then findPort (port + 1) else port

    let port = findPort 8083

    let serverConfig = 
        { defaultConfig with
           homeFolder = Some (FullName config.OutputDir)
           bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" port ]
        }
    
    let indexPath (ctx:Suave.Http.HttpContext) = ctx.runtime.homeDirectory + ctx.request.path + "/index.html"

    let indexExists (ctx:Suave.Http.HttpContext) = async {
        let file = ctx |> indexPath
        match System.IO.File.Exists file with
        | true -> return Some(ctx)
        | false -> return None
    }

    let app =
      choose [
        Filters.path "/websocket" >=> handShake socketHandler
        Writers.setHeader "Cache-Control" "no-cache, no-store, must-revalidate"
        >=> Writers.setHeader "Pragma" "no-cache"
        >=> Writers.setHeader "Expires" "0"
        >=> choose [
                indexExists >=> (context(fun ctx -> ctx |> indexPath |> browseFileHome))
                browseHome 
            ]
        ]
    startWebServerAsync serverConfig app |> snd |> Async.Start
    Process.Start (sprintf "http://localhost:%d/" port) |> ignore

Target "Run" (fun _ ->
    generateWithSockets()
    use watcher = !! (config.SourceDir + "/**/*.*") |> WatchChanges handleWatcherEvents
    startWebServer ()
    traceImportant "Waiting for content edits. Press any key to stop."
    System.Console.ReadKey() |> ignore
    watcher.Dispose()
)

// start build
RunTargetOrDefault "?"