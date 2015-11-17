// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r @"packages/FAKE/tools/FakeLib.dll"

open Fake
open Fake.Git
open Fake.ReleaseNotesHelper
open Fake.UserInputHelper
open System
open System.IO

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "iati-xl2xml"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "Aid project data reporting."

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = "Aid project data reporting to the IATI standard."

// List of author names (for NuGet package)
let authors = [ "Mark Townsend" ]

// Tags for your project (for NuGet package)
let tags = "Excel VBA IATI WaterAid"

// File system information 
let solutionFile  = "xl2xml.xlsm"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "WaterAid" 
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "iati-xl2xml"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/wateraid"

// Read additional information from the release notes document
let release = LoadReleaseNotes "RELEASE_NOTES.md"

// The name of the source application
let sourceFile = @"C:/mark/excel/iati-xl2xml/src"

// --------------------------------------------------------------------------------------
// Clean build results
Target "CleanSource" (fun _ ->
    !! "src/*.*"
        -- "*.xlsm"
        |> Seq.iter DeleteFile
)

Target "CleanDocs" (fun _ ->
    CleanDirs ["docs/output"]
)

//---------------------------------------------------------------------------------------
// Extract the source files from the application
let getSource (filename: string) = 
    let outcome = executeFSIWithScriptArgsAndReturnMessages "docs/tools/vbaprojecthelper.fsx" [|filename|]
    match outcome with
    | (true, _) -> trace "source files successfully extracted."
    | (false, _) -> snd(outcome)|> Seq.iter (fun f -> traceError f.Message)
    ()

Target "ExtractSource" (fun _ ->
   getSource @"C:\mark\excel\iati-xl2xml\src\xl2xml.xlsm"
)

// //---------------------------------------------------------------------------------------
// // Extract the markdown from the source files
// Target "ReferenceDocumentation" (fun _ ->
//     !! "src/*.*"
//         -- "*.xlsm"
//         |> GetPublicComments
//         |> CopyFile "docs/contents/api"
//         |> DeleteFile
// )

// --------------------------------------------------------------------------------------
// Generate the documentation

let generateHelp' fail debug =
    let args =
        if debug then ["--define:HELP"]
        else ["--define:RELEASE"; "--define:HELP"]
    if executeFSIWithArgs "docs/tools" "generate.fsx" args [] then
        traceImportant "Help generated"
    else
        if fail then
            failwith "generating help documentation failed"
        else
            traceImportant "generating help documentation failed"

let generateHelp fail =
    generateHelp' fail false

Target "GenerateHelp" (fun _ ->
    DeleteFile "docs/content/release-notes.md"
    CopyFile "docs/content/" "RELEASE_NOTES.md"
    Rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    DeleteFile "docs/content/license.md"
    CopyFile "docs/content/" "LICENSE.txt"
    Rename "docs/content/license.md" "docs/content/LICENSE.txt"

    generateHelp true
)

Target "GenerateHelpDebug" (fun _ ->
    DeleteFile "docs/content/release-notes.md"
    CopyFile "docs/content/" "RELEASE_NOTES.md"
    Rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

    DeleteFile "docs/content/license.md"
    CopyFile "docs/content/" "LICENSE.txt"
    Rename "docs/content/license.md" "docs/content/LICENSE.txt"

    generateHelp' true true
)

let createIndexFsx lang =
    let content = """(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../../bin"

(**
WaterAid IATI XL2XML Project ({0})
=========================
*)
"""
    let targetDir = "docs/content" @@ lang
    let targetFile = targetDir @@ "index.fsx"
    ensureDirectory targetDir
    System.IO.File.WriteAllText(targetFile, System.String.Format(content, lang))


// --------------------------------------------------------------------------------------
// Release Scripts

Target "ReleaseDocs" (fun _ ->
    let tempDocsDir = "temp/gh-pages"
    CleanDir tempDocsDir
    Repository.cloneSingleBranch "" (gitHome + "/" + gitName + ".git") "gh-pages" tempDocsDir

    CopyRecursive "docs/output" tempDocsDir true |> tracefn "%A"
    StageAll tempDocsDir
    Git.Commit.Commit tempDocsDir (sprintf "Update generated documentation for version %s" release.NugetVersion)
    Branches.push tempDocsDir
)

#load "paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit

Target "Release" (fun _ ->
    let user =
        match getBuildParam "github-user" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserInput "Username: "
    let pw =
        match getBuildParam "github-pw" with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getUserPassword "Password: "
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function None -> gitHome + "/" + gitName | Some (s: string) -> s.Split().[0]

    StageAll ""
    Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.pushBranch "" remote (Information.getBranchName "")

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" remote release.NugetVersion
    
    // release on github
    createClient user pw
    |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes 
    // TODO: |> uploadFile "PATH_TO_FILE"
    |> releaseDraft
    |> Async.RunSynchronously
)


// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Clean"
   ==> "CleanSource"
   ==> "CleanDocs"
    
"ReleaseDocs"
  ==> "Release"

RunTargetOrDefault "All"