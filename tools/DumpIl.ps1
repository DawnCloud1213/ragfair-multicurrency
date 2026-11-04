param(
    [string]$GameDir = $env:SPT_GAME_DIR,
    [string]$AssemblyPath = '',
    [string]$CecilPath = ''
)

$ErrorActionPreference = 'Stop'

if (-not $AssemblyPath) {
    if (-not $GameDir) { throw 'Set SPT_GAME_DIR (or pass -GameDir) to your SPT client folder.' }
    $AssemblyPath = Join-Path $GameDir 'EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll'
}

if (-not $CecilPath) {
    if (-not $GameDir) { throw 'Set SPT_GAME_DIR (or pass -GameDir) to your SPT client folder.' }
    $CecilPath = Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll'
}
# Loading by path trips the "assembly from a network location" CAS check here, so read the
# bytes and load from memory instead.
$cecilBytes = [System.IO.File]::ReadAllBytes($CecilPath)
[void][System.Reflection.Assembly]::Load($cecilBytes)

Write-Host "Reading module (may take a while)..."
$mod = [Mono.Cecil.ModuleDefinition]::ReadModule($AssemblyPath, (New-Object Mono.Cecil.ReaderParameters -Property @{ ReadSymbols = $false }))

function Dump-Method([string]$typeName, [string]$methodName) {
    $type = $mod.GetType($typeName)
    if ($null -eq $type) { Write-Host "TYPE NOT FOUND: $typeName"; return }
    $methods = @($type.Methods | Where-Object { $_.Name -eq $methodName })
    if ($methods.Count -eq 0) { Write-Host "METHOD NOT FOUND: $typeName::$methodName"; return }

    foreach ($m in $methods) {
        Write-Host ""
        Write-Host ("===== " + $typeName + "::" + $m.Name + " =====")
        if (-not $m.HasBody) { Write-Host "  (no body)"; continue }
        foreach ($i in $m.Body.Instructions) {
            $op = $i.OpCode.ToString()
            $operand = $i.Operand
            $text = ""
            if ($null -ne $operand) {
                if ($operand -is [Mono.Cecil.MethodReference]) {
                    $text = $operand.DeclaringType.Name + "::" + $operand.Name
                } elseif ($operand -is [Mono.Cecil.FieldReference]) {
                    $text = $operand.DeclaringType.Name + "::" + $operand.Name
                } else {
                    $text = [string]$operand
                }
            }
            Write-Host ("  IL_" + $i.Offset.ToString("x4") + ": " + $op + " " + $text)
        }
    }
}

Dump-Method 'EFT.UI.Ragfair.AddOfferWindow' 'PricesPanelOnOnPriceClick'
Dump-Method 'EFT.UI.Ragfair.RequirementView' 'SetValue'
Dump-Method 'EFT.UI.Ragfair.RequirementView' 'AtInput'
