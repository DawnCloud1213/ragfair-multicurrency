param(
    [string]$GameDir = "D:\free games\EFT_0821",
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Join-Path $repoRoot "src\RagfairMultiCurrency"
$outputDir = Join-Path $repoRoot "dist\BepInEx\plugins\RagfairMultiCurrency"
$managedDir = Join-Path $GameDir "EscapeFromTarkov_Data\Managed"
$bepInExCoreDir = Join-Path $GameDir "BepInEx\core"

$cscCandidates = @(
    (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
    (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe")
)

$csc = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $csc) {
    throw "Could not find csc.exe (.NET Framework compiler)."
}

$requiredAssemblies = @(
    (Join-Path $managedDir "mscorlib.dll"),
    (Join-Path $managedDir "System.dll"),
    (Join-Path $managedDir "System.Core.dll"),
    (Join-Path $managedDir "netstandard.dll"),
    (Join-Path $managedDir "Assembly-CSharp.dll"),
    (Join-Path $managedDir "ItemComponent.Types.dll"),
    (Join-Path $managedDir "ItemTemplate.Types.dll"),
    (Join-Path $bepInExCoreDir "BepInEx.dll"),
    (Join-Path $bepInExCoreDir "0Harmony.dll"),
    (Join-Path $managedDir "Sirenix.OdinInspector.Attributes.dll"),
    (Join-Path $managedDir "Sirenix.Serialization.dll"),
    (Join-Path $managedDir "Sirenix.Utilities.dll"),
    (Join-Path $managedDir "UnityEngine.dll"),
    (Join-Path $managedDir "UnityEngine.CoreModule.dll"),
    (Join-Path $managedDir "UnityEngine.TextRenderingModule.dll"),
    (Join-Path $managedDir "UnityEngine.TextCoreFontEngineModule.dll"),
    (Join-Path $managedDir "UnityEngine.TextCoreTextEngineModule.dll"),
    (Join-Path $managedDir "UnityEngine.UIModule.dll"),
    (Join-Path $managedDir "UnityEngine.UI.dll"),
    (Join-Path $managedDir "Unity.TextMeshPro.dll")
)

foreach ($assembly in $requiredAssemblies) {
    if (-not (Test-Path -LiteralPath $assembly)) {
        throw "Required assembly not found: $assembly"
    }
}

$sourceFiles = Get-ChildItem -LiteralPath $projectDir -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" } |
    ForEach-Object { $_.FullName }

if (-not $sourceFiles) {
    throw "No C# source files found under $projectDir"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$referenceArgs = $requiredAssemblies | ForEach-Object { "/reference:$($_)" }
$cscArgs = @(
    "/nologo",
    "/noconfig",
    "/nostdlib+",
    "/target:library",
    "/langversion:5",
    "/optimize+",
    "/debug:pdbonly",
    "/define:TRACE",
    "/out:$outputDir\RagfairMultiCurrency.dll",
    "/pdb:$outputDir\RagfairMultiCurrency.pdb"
) + $referenceArgs + $sourceFiles

Write-Host "Compiling RagfairMultiCurrency..."
& $csc $cscArgs

if ($LASTEXITCODE -ne 0) {
    throw "csc.exe failed with exit code $LASTEXITCODE"
}

Write-Host "Built: $outputDir\RagfairMultiCurrency.dll"

if ($Deploy) {
    $deployDir = Join-Path $GameDir "BepInEx\plugins\RagfairMultiCurrency"
    New-Item -ItemType Directory -Force -Path $deployDir | Out-Null
    Copy-Item -LiteralPath (Join-Path $outputDir "RagfairMultiCurrency.dll") -Destination $deployDir -Force
    Write-Host "Deployed: $deployDir\RagfairMultiCurrency.dll"
}
