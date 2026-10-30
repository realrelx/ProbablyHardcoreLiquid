# Probably Hardcore Liquid - build script
# Compiles with the .NET Framework 4 csc (C#5) against the game assemblies.
# 注意：必须引用游戏自带的 netstandard.dll 门面 + mscorlib.dll，
# 且用 /noconfig 避免框架目录的 System.dll 与游戏目录冲突。
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$game = 'D:\steam\steamapps\common\Probably Stolen'
$managed = Join-Path $game 'Probably Stolen_Data\Managed'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $game)) { Write-Host "Game not found: $game" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $csc)) { Write-Host "csc.exe not found: $csc" -ForegroundColor Red; exit 1 }

$refDlls = @('netstandard.dll', 'mscorlib.dll', 'UnityEngine.CoreModule.dll', 'Assembly-CSharp.dll', '0Harmony.dll')
foreach ($dll in $refDlls) {
    $p = Join-Path $managed $dll
    if (-not (Test-Path $p)) { Write-Host "Missing reference: $p" -ForegroundColor Red; exit 1 }
}

$refs = @()
foreach ($dll in $refDlls) {
    $refs += '/r:' + '"' + (Join-Path $managed $dll) + '"'
}

$srcFiles = Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | Sort-Object Name
if (-not $srcFiles) { Write-Host 'No .cs files in src\' -ForegroundColor Red; exit 1 }
$src = @()
foreach ($f in $srcFiles) { $src += '"' + $f.FullName + '"' }

$out = Join-Path $root 'ProbablyHardcoreLiquid.dll'

Write-Host 'Compiling ProbablyHardcoreLiquid.dll ...'
& $csc /nologo /noconfig /target:library /langversion:5 /nostdlib+ "/out:$out" $refs $src
if ($LASTEXITCODE -ne 0) {
    Write-Host "Compile failed (exit $LASTEXITCODE)" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host 'Compile OK.' -ForegroundColor Green

$modDir = Join-Path $game ('Mods\' + 'ProbablyHardcoreLiquid')
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
Copy-Item $out $modDir -Force
Copy-Item (Join-Path $root 'manifest.xml') $modDir -Force
Write-Host "Installed to: $modDir" -ForegroundColor Green
Write-Host 'Launch the game, enable the mod, then check the Player.log for [HardcoreLiquid] lines.'
