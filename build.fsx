// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r "paket:
    storage: none
    source https://api.nuget.org/v3/index.json
    nuget Fake.Core.Target
    nuget Fake.IO.FileSystem
    nuget Fake.Core.Environment
    nuget Fake.DotNet.Cli
    nuget Fake.DotNet.MSBuild
    nuget Fake.DotNet.AssemblyInfoFile
    nuget Fake.DotNet.Paket
    nuget Fake.Tools.Git
    nuget Fake.Core.Target
    nuget Fake.Core.ReleaseNotes
    nuget Fake.DotNet.Fsi
    nuget Fake.Core.UserInput
    nuget Fake.Api.GitHub
    nuget FSharp.Formatting //"

open System
open System.IO
open Fake.IO
open Fake.Api
open Fake.DotNet
open Fake.Tools
open Fake.Core
open Fake.Tools.Git
open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators
open Fake.Core.TargetOperators
#if !FAKE
#load ".fake/build.fsx/intellisense.fsx"
#r "netstandard"
#endif

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "GslCore"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "Core library and basic plug-ins for the GSL compiler."

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = "Core library and basic plug-ins for the Amyris Genotype Specification Language (GSL) compiler."

// List of author names (for NuGet package)
let authors = [ "Darren Platt"; "Chris Macklin" ]

// Tags for your project (for NuGet package)
let tags = "GSL amyris compiler'"

// File system information
let solutionFile  = "GslCore.sln"

// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "tests/**/bin/Release/netcoreapp2.0/*Tests*.dll"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "Update GitHome in build.fsx"
let gitHome = sprintf "%s/%s" "https://github.com" gitOwner

// The name of the project on GitHub
let gitName = "GslCore"

// The url for the raw files hosted
let gitRaw = Environment.environVarOrDefault "gitRaw" "https://raw.githubusercontent.com/Update GitHome in build.fsx"

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
let release = ReleaseNotes.load "RELEASE_NOTES.md"

// Helper active pattern for project types
let (|Fsproj|Csproj|Vbproj|Shproj|) (projFileName:string) =
    match projFileName with
    | f when f.EndsWith("fsproj") -> Fsproj
    | f when f.EndsWith("csproj") -> Csproj
    | f when f.EndsWith("vbproj") -> Vbproj
    | f when f.EndsWith("shproj") -> Shproj
    | _                           -> failwith (sprintf "Project file %s not supported. Unknown project type." projFileName)

// Generate assembly info files with the right version & up-to-date information
Target.create "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [ AssemblyInfo.Title (projectName)
          AssemblyInfo.Product project
          AssemblyInfo.Description summary
          AssemblyInfo.Version release.AssemblyVersion
          AssemblyInfo.InformationalVersion (Git.Information.getCurrentHash())
          AssemblyInfo.FileVersion release.AssemblyVersion ]

    let getProjectDetails projectPath =
        let projectName = Path.GetFileNameWithoutExtension(projectPath)
        ( projectPath,
          projectName,
          Path.GetDirectoryName(projectPath),
          (getAssemblyInfoAttributes projectName)
        )

    !! "src/**/*.??proj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, _, folderName, attributes) ->
        match projFileName with
        | Fsproj -> AssemblyInfoFile.createFSharp (folderName </> "AssemblyInfo.fs") attributes
        | Csproj -> AssemblyInfoFile.createCSharp ((folderName </> "Properties") </> "AssemblyInfo.cs") attributes
        | Vbproj -> AssemblyInfoFile.createVisualBasic ((folderName </> "My Project") </> "AssemblyInfo.vb") attributes
        | Shproj -> ()
        )
)

// Copies binaries from default VS location to expected bin folder
// But keeps a subdirectory structure for each project in the
// src folder to support multiple project outputs
Target.create "CopyBinaries" (fun _ ->
    !! "src/**/*.??proj"
    -- "src/**/*.shproj"
    |>  Seq.map (fun f -> ((Path.GetDirectoryName f) </> "bin/Release", "bin" </> (Path.GetFileNameWithoutExtension f)))
    |>  Seq.iter (fun (fromDir, toDir) -> Shell.copyDir toDir fromDir (fun _ -> true))
)

// --------------------------------------------------------------------------------------
// Clean build results

Target.create "Clean" (fun _ ->
    Shell.cleanDirs
        [ "bin"
          "temp"
          "docs/output"
          "src/GslCore/bin"
          "tests/GslCore.Tests/bin" ]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target.create "Build" (fun _ ->
    !! "src/**/*.??proj"
    ++ "tests/**/*.??proj"
    |> Array.ofSeq
    |> Array.iter (fun project ->
        project
        |> DotNet.build (fun buildOptions ->
            { buildOptions with
                Configuration = DotNet.BuildConfiguration.Release })))


// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target.create "RunTests" (fun _ ->
    DotNet.test
        (fun parameters ->
            { parameters with Configuration = DotNet.BuildConfiguration.Release })
        "tests/GslCore.Tests"
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target.create "NuGet" (fun _ ->
    Paket.pack(fun p ->
        { p with
            OutputPath = "bin"
            Version = release.NugetVersion
            MinimumFromLockFile = true
            ReleaseNotes = String.toLines release.Notes})
)

Target.create "PublishNuget" (fun _ ->
    Paket.push(fun p ->
        { p with
            WorkingDir = "bin" })
)


// --------------------------------------------------------------------------------------
// Generate the documentation

let docSrcTools = "docs" </> "tools"

// Documentation
let buildDocumentationTarget () =
    Trace.trace "Building documentation, this could take some time, please wait..."
    let exitCode, errors =
        Fsi.exec
            id
            (docSrcTools </> "generate.fsx")
            []
    if exitCode <> 0 then
        failwithf "generating reference documentation failed: %A" errors
    ()
    
let generateHelp' fail =
    try
        buildDocumentationTarget ()
        Trace.traceImportant "Help generated"
    with
    | _ when not fail ->
        Trace.traceImportant "generating help documentation failed"

let generateHelp fail =
    generateHelp' fail

Target.create "GenerateHelp" (fun _ ->
    "docs/content/release-notes.md" |> File.delete
    "RELEASE_NOTES.md" |> Shell.copyFile "docs/content/"
    "docs/content/RELEASE_NOTES.md" |> Shell.rename "docs/content/release-notes.md"

    "docs/content/license.md" |> File.delete
    "LICENSE.txt" |> Shell.copyFile "docs/content/"
    "docs/content/LICENSE.txt" |> Shell.rename "docs/content/license.md"

    generateHelp true
)

Target.create "GenerateHelpDebug" (fun _ ->
    "docs/content/release-notes.md" |> File.delete
    "RELEASE_NOTES.md" |> Shell.copyFile "docs/content/"
    "docs/content/RELEASE_NOTES.md" |> Shell.rename "docs/content/release-notes.md"

    "docs/content/license.md" |> File.delete
    "LICENSE.txt" |> Shell.copyFile "docs/content/"
    "docs/content/LICENSE.txt" |> Shell.rename "docs/content/license.md"

    generateHelp' true
)

Target.create "KeepRunning" (fun _ ->
    use watcher = !! "docs/content/**/*.*" |> ChangeWatcher.run (fun _ ->
         generateHelp' true
    )

    Trace.traceImportant "Waiting for help edits. Press any key to stop."

    System.Console.ReadKey() |> ignore

    watcher.Dispose()
)

Target.create "GenerateDocs" ignore

let createIndexFsx lang =
    let content = """(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use
// it to define helpers that you do not want to show in the documentation.
#I "../../../bin"

(**
F# Project Scaffold ({0})
=========================
*)
"""
    let targetDir = "docs/content" </> lang
    let targetFile = targetDir </> "index.fsx"
    Directory.ensure targetDir
    File.WriteAllText(targetFile, System.String.Format(content, lang))

Target.create "AddLangDocs" (fun _ ->
    let args = System.Environment.GetCommandLineArgs()
    if args.Length < 4 then
        failwith "Language not specified."

    args.[3..]
    |> Seq.iter (fun lang ->
        if lang.Length <> 2 && lang.Length <> 3 then
            failwithf "Language must be 2 or 3 characters (ex. 'de', 'fr', 'ja', 'gsw', etc.): %s" lang

        let templateFileName = "template.cshtml"
        let templateDir = "docs/tools/templates"
        let langTemplateDir = templateDir </> lang
        let langTemplateFileName = langTemplateDir </> templateFileName

        if langTemplateFileName |> File.exists then
            failwithf "Documents for specified language '%s' have already been added." lang

        Directory.ensure langTemplateDir
        Shell.copy langTemplateDir [ templateDir </> templateFileName ]

        createIndexFsx lang)
)

// --------------------------------------------------------------------------------------
// Release Scripts

Target.create "ReleaseDocs" (fun _ ->
    let tempDocsDir = "temp/gh-pages"
    Shell.cleanDir tempDocsDir
    
    Repository.cloneSingleBranch "" (gitHome + "/" + gitName + ".git") "gh-pages" tempDocsDir

    Shell.copyRecursive "docs/output" tempDocsDir true |> Trace.tracef "%A"
    Staging.stageAll tempDocsDir
    Commit.exec tempDocsDir (sprintf "Update generated documentation for version %s" release.NugetVersion)
    Branches.push tempDocsDir
)

Target.create "Release" (fun _ ->
    let user =
        match Environment.environVarOrNone "github-user" with
        | Some(s) when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserInput "Username: "
    let pw =
        match Environment.environVarOrNone "github-pw" with
        | Some(s) when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserPassword "Password: "
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function None -> gitHome + "/" + gitName | Some (s: string) -> s.Split().[0]

    Staging.stageAll ""
    Commit.exec "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.pushBranch "" remote (Information.getBranchName "")

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" remote release.NugetVersion
    // release on github
    GitHub.createClient user pw
    |> GitHub.draftNewRelease gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes
    // TODO: |> uploadFile "PATH_TO_FILE"
    |> GitHub.publishDraft
    |> Async.RunSynchronously
)

Target.create "BuildPackage" ignore

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target.create "All" ignore

"AssemblyInfo"
  ==> "Build"
  ==> "CopyBinaries"
  ==> "RunTests"
  ==> "GenerateDocs"
  ==> "NuGet"
  ==> "BuildPackage"
  ==> "All"
  =?> ("ReleaseDocs", BuildServer.isLocalBuild)

"GenerateHelp"
  ==> "GenerateDocs"

"GenerateHelpDebug"
  ==> "KeepRunning"

"Clean"
  ==> "Release"

"BuildPackage"
  ==> "PublishNuget"
  ==> "Release"

"ReleaseDocs"
  ==> "Release"

Target.runOrDefault "All"
