#r "paket: groupref build //"
#load "./.fake/build.fsx/intellisense.fsx"

#if !FAKE
#r "netstandard"
#endif

open System
open System.IO

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
 
Target.initEnvironment ()

let libPath = "./src"
let testsPath = "./test"

let platformTool tool winTool =
    let tool = if Environment.isUnix then tool else winTool
    match ProcessUtils.tryFindFileOnPath tool with
    | Some t -> t
    | _ ->
        let errorMsg =
            tool + " was not found in path. " +
            "Please install it and make sure it's available from your path. " +
            "See https://safe-stack.github.io/docs/quickstart/#install-pre-requisites for more info"
        failwith errorMsg
        
let nodeTool = platformTool "node" "node.exe"
let mutable dotnetCli = "dotnet"

let run cmd args workingDir =
    let arguments = args |> String.split ' ' |> Arguments.OfArgs
    Command.RawCommand (cmd, arguments)
    |> CreateProcess.fromCommand
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore

let runDotNet cmd workingDir =
    let result =
        DotNet.exec (DotNet.Options.withWorkingDirectory workingDir) cmd ""
    if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir

let delete file =
    if File.Exists(file)
    then File.Delete file
    else ()

let cleanBundles() =
    Path.Combine("public", "bundle.js")
        |> Path.GetFullPath
        |> delete
    Path.Combine("public", "bundle.js.map")
        |> Path.GetFullPath
        |> delete

let cleanBinObjInPaths paths =  
    paths
    |> List.collect (fun path ->
         [ path </> "bin"
           path </> "obj" ])
    |> Shell.cleanDirs

Target.create "Clean" <| fun _ ->
    cleanBinObjInPaths [testsPath; libPath]
    cleanBundles()

Target.create "InstallNpmPackages" (fun _ ->
  printfn "Node version:"
  run "node" "--version" __SOURCE_DIRECTORY__
  run "npm" "install" __SOURCE_DIRECTORY__
)

Target.create "RunLiveTests" <| fun _ ->
  run "npm" "run start" testsPath

let publish projectPath = fun () ->
    cleanBinObjInPaths [projectPath]
    run dotnetCli "restore --no-cache" projectPath
    run dotnetCli "pack -c Release" projectPath
    let nugetKey =
        match Environment.environVarOrNone "NUGET_KEY" with
        | Some nugetKey -> nugetKey
        | None -> failwith "The Nuget API key must be set in a NUGET_KEY environmental variable"
    let nupkg =
        Directory.GetFiles(projectPath </> "bin" </> "Release")
        |> Seq.head
        |> Path.GetFullPath

    let pushCmd = sprintf "nuget push %s -s nuget.org -k %s" nupkg nugetKey
    runDotNet pushCmd projectPath

Target.create "PublishNuget" <| fun _ ->
    (publish libPath)()

Target.create "CompileFableTestProject" <| fun _ ->
    run "npm" "run build" testsPath

Target.create "RunTests" <| fun _ ->
    printfn "Building %s with Fable" testsPath
    run "npm" "test" "."
    run "npm" "run headless-tests" "."
    cleanBundles()

open Fake.Core.TargetOperators

"Clean"
  ==> "InstallNpmPackages"
  ==> "RunLiveTests"

"Clean"
 ==> "InstallNpmPackages"
 ==> "RunTests"

Target.runOrDefaultWithArguments "RunTests"