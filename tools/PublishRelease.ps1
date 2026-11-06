<#
.SYNOPSIS
    One-shot release: build locally, tag, push the tag, publish the GitHub release with the dll attached.

.DESCRIPTION
    Everything runs on the workstation on purpose. Compiling this mod needs the EFT/Unity
    reference assemblies (Assembly-CSharp.dll, BepInEx.dll, 0Harmony.dll, Sirenix.*.dll, ...)
    that live inside a local SPT install; those are third-party files and are never committed,
    so there is no CI compile step at all - CI cannot legally get the references.

    The script refuses to run unless the working tree is clean and HEAD is already pushed,
    so the tag always points at a commit the remote really has.

.PARAMETER Version
    Version being released, with or without the leading v. Must be three dotted numbers (0.4.6).

.PARAMETER GameDir
    SPT client folder used by build.ps1. Defaults to $env:SPT_GAME_DIR.

.PARAMETER NotesFile
    Use this markdown file as the release body instead of the generated one.

.PARAMETER SkipBuild
    Reuse the dll already sitting in dist\ instead of recompiling.

.PARAMETER AllowDirty
    Do not abort when the working tree has uncommitted changes.

.PARAMETER DryRun
    Print every command that would run and change nothing on the remote.

.EXAMPLE
    $env:SPT_GAME_DIR = 'D:\free games\EFT_0821'
    powershell -ExecutionPolicy Bypass -File tools\PublishRelease.ps1 -Version 0.4.6

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\PublishRelease.ps1 -Version 0.4.6 -SkipBuild -DryRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$GameDir = $env:SPT_GAME_DIR,
    [string]$NotesFile = '',
    [switch]$SkipBuild,
    [switch]$AllowDirty,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$scriptDir    = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot     = Split-Path -Parent $scriptDir
$buildScript  = Join-Path $repoRoot 'build.ps1'
$dllPath      = Join-Path $repoRoot 'dist\BepInEx\plugins\RagfairMultiCurrency\RagfairMultiCurrency.dll'
$templatePath = Join-Path $scriptDir 'release-notes-template.md'

$ver = $Version.TrimStart('v', 'V')
if ($ver -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must look like 0.4.6 (got '$Version')."
}
$tag = "v$ver"

function Write-Plan {
    param([string]$Text)
    Write-Host "    $Text"
}

# Runs a native command from the repo root and aborts on a non-zero exit code.
function Invoke-Native {
    param(
        [string]$Label,
        [string]$Exe,
        [string[]]$Arguments
    )
    Write-Host "==> $Label"
    Write-Plan "$Exe $($Arguments -join ' ')"
    if ($DryRun) { return }
    Push-Location $repoRoot
    try {
        & $Exe @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "$Exe failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}

# Read-only git helper: returns the trimmed output, throws when git itself fails.
function Get-GitValue {
    param([string[]]$Arguments, [switch]$TolerateFailure)
    Push-Location $repoRoot
    try {
        $output = & git @Arguments 2>$null
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($code -ne 0 -and -not $TolerateFailure) {
        throw "git $($Arguments -join ' ') failed with exit code $code"
    }
    if ($code -ne 0) { return $null }
    return ($output | Out-String).Trim()
}

Write-Host "Publishing $tag from $repoRoot"
if ($DryRun) { Write-Host '(dry run: nothing will be built, tagged or uploaded)' }

# ---------------------------------------------------------------- pre-flight
Write-Host '--- pre-flight ---'

$branch = Get-GitValue -Arguments @('branch', '--show-current')
if (-not $branch) {
    throw 'Detached HEAD or not a git repository. Check out a branch first.'
}
Write-Plan "branch: $branch"

$dirty = Get-GitValue -Arguments @('status', '--porcelain')
if ($dirty -and -not $AllowDirty) {
    throw "Working tree is dirty; commit or stash first (or pass -AllowDirty).`n$dirty"
}
Write-Plan "working tree: $(if ($dirty) { 'dirty (allowed)' } else { 'clean' })"

Invoke-Native -Label 'Fetch remote refs' -Exe 'git' -Arguments @('fetch', 'origin', '--tags', '--prune')

$head = Get-GitValue -Arguments @('rev-parse', 'HEAD')
$remoteHead = Get-GitValue -Arguments @('rev-parse', "refs/remotes/origin/$branch") -TolerateFailure
if (-not $remoteHead) {
    throw "origin/$branch does not exist yet; push the branch before releasing."
}
if ($head -ne $remoteHead) {
    $ahead = Get-GitValue -Arguments @('rev-list', '--count', "origin/$branch..HEAD")
    throw "HEAD ($head) is not on origin/$branch ($remoteHead, $ahead commit(s) ahead). Push the branch first - the tag must point at a commit the remote has."
}
Write-Plan "HEAD: $head (in sync with origin/$branch)"

$localTag = Get-GitValue -Arguments @('tag', '--list', $tag)
$remoteTag = Get-GitValue -Arguments @('ls-remote', '--tags', 'origin', "refs/tags/$tag") -TolerateFailure
if ($localTag -or $remoteTag) {
    throw "Tag $tag already exists. Bump the version, or delete the stale tag first."
}
Write-Plan "tag ${tag}: free"

if (-not $SkipBuild -and -not $GameDir) {
    throw 'Set SPT_GAME_DIR (or pass -GameDir) so build.ps1 can find the client assemblies; use -SkipBuild to reuse dist\.'
}

# ------------------------------------------------------------- release notes
Write-Host '--- release notes ---'

if ($NotesFile) {
    $notesBody = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesFile).Path)
    Write-Plan "using $NotesFile"
}
else {
    $allTags = Get-GitValue -Arguments @('tag', '--sort=-creatordate') -split "`r?`n"
    $prevTag = $allTags | Where-Object { $_ -and $_ -ne $tag } | Select-Object -First 1
    if ($prevTag) {
        $range = "$prevTag..HEAD"
    }
    else {
        $range = 'HEAD'
    }
    $log = Get-GitValue -Arguments @('log', $range, '--no-merges', '--pretty=format:- %s')
    if (-not $log) { $log = '- Maintenance changes.' }
    Write-Plan "changes since: $(if ($prevTag) { $prevTag } else { 'repository start' })"

    if (-not (Test-Path -LiteralPath $templatePath)) {
        throw "Missing notes template: $templatePath"
    }
    $template = [System.IO.File]::ReadAllText($templatePath)
    $notesBody = $template.Replace('{{TAG}}', $tag).Replace('{{CHANGES}}', $log)
}

$notesPath = Join-Path $env:TEMP "ragfair-release-$tag.md"
if ($DryRun) {
    Write-Plan "notes would be written to: $notesPath"
    Write-Host '--- notes preview ---'
    ($notesBody -split "`n" | Select-Object -First 15) | ForEach-Object { Write-Host "    $_" }
    Write-Host '    ...'
}
else {
    [System.IO.File]::WriteAllText($notesPath, $notesBody, (New-Object System.Text.UTF8Encoding($false)))
    Write-Plan "notes written to: $notesPath"
}

# -------------------------------------------------------------- build step
Write-Host '--- build ---'

if ($SkipBuild) {
    Write-Plan 'skipped (-SkipBuild)'
}
else {
    Invoke-Native -Label "Compile $ver" -Exe 'powershell' -Arguments @('-ExecutionPolicy', 'Bypass', '-File', $buildScript, '-GameDir', $GameDir)
}

if ($DryRun) {
    Write-Plan "expecting dll at: $dllPath"
}
else {
    if (-not (Test-Path -LiteralPath $dllPath)) {
        throw "Build produced no dll at $dllPath"
    }
    $dllHash = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash
    Write-Plan "dll: $dllPath"
    Write-Plan "sha256: $dllHash"
}

# ----------------------------------------------------- tag, push, publish
Write-Host '--- publish ---'

Invoke-Native -Label "Create annotated tag $tag" -Exe 'git' -Arguments @('tag', '-a', $tag, '-m', "$tag release")
Invoke-Native -Label "Push tag $tag" -Exe 'git' -Arguments @('push', 'origin', "refs/tags/$tag")
Invoke-Native -Label "Create GitHub release $tag" -Exe 'gh' -Arguments @('release', 'create', $tag, $dllPath, '--title', $tag, '--notes-file', $notesPath, '--latest')

# ---------------------------------------------------------------- verify
Write-Host '--- verify ---'

Invoke-Native -Label 'Release metadata' -Exe 'gh' -Arguments @('release', 'view', $tag, '--json', 'tagName,name,isDraft,isPrerelease,assets,url')
$verifyDir = Join-Path $env:TEMP "ragfair-verify-$tag"
if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path $verifyDir | Out-Null
}
Invoke-Native -Label 'Asset is downloadable' -Exe 'gh' -Arguments @('release', 'download', $tag, '--pattern', '*.dll', '--dir', $verifyDir, '--clobber')

if ($DryRun) {
    Write-Host ''
    Write-Host 'Dry run finished: nothing was built, tagged or uploaded.'
}
else {
    $downloaded = Join-Path $env:TEMP "ragfair-verify-$tag\RagfairMultiCurrency.dll"
    if (Test-Path -LiteralPath $downloaded) {
        $remoteHash = (Get-FileHash -LiteralPath $downloaded -Algorithm SHA256).Hash
        if ($remoteHash -eq $dllHash) {
            Write-Host "OK: remote dll matches the local build ($remoteHash)"
        }
        else {
            throw "Remote dll hash $remoteHash does not match local build $dllHash"
        }
    }
    Write-Host ''
    Write-Host "Released $tag"
}
