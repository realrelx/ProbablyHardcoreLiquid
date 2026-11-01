<#
  ProbablyHardcoreLiquid - 本地构建脚本（csc 通道）
  本机没有 .NET SDK，走 C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe（C#5），
  引用游戏 Managed 下的 netstandard facade + mscorlib + Unity 模块 + Assembly-CSharp + 0Harmony。
  产物：build\<Version>\ProbablyHardcoreLiquid.dll （不写游戏目录，安装交给 deploy.ps1）
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$GamePath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $root 'manifest.xml'
$srcDir = Join-Path $root 'src'
$texDir = Join-Path $root 'Textures\Items'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $manifestPath)) { throw "manifest.xml 不存在: $manifestPath" }
if (-not (Test-Path $srcDir)) { throw "src 目录不存在: $srcDir" }
if (-not (Test-Path $csc)) { throw "找不到 csc.exe: $csc" }

[xml]$manifest = Get-Content $manifestPath -Encoding UTF8
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $manifest.ModManifest.ModVersion }
if ([string]::IsNullOrWhiteSpace($Version)) { throw 'manifest.xml 缺少 ModVersion，且未传 -Version' }
$manifest.ModManifest.ModVersion = $Version

if ([string]::IsNullOrWhiteSpace($GamePath)) { $GamePath = $env:PROBABLY_STOLEN_PATH }
if ([string]::IsNullOrWhiteSpace($GamePath)) {
    throw '请用 -GamePath 或环境变量 PROBABLY_STOLEN_PATH 指定游戏安装目录，例如：' +
          "`n  .\build.ps1 -GamePath 'D:\steam\steamapps\common\Probably Stolen'" +
          "`n  `$env:PROBABLY_STOLEN_PATH = 'D:\steam\steamapps\common\Probably Stolen'"
}

$managed = Join-Path $GamePath 'Probably Stolen_Data\Managed'
if (-not (Test-Path $managed)) { throw "找不到游戏 Managed 目录（-GamePath 指向游戏根目录）: $managed" }

$refDlls = @(
    'netstandard.dll', 'mscorlib.dll',
    'UnityEngine.CoreModule.dll', 'UnityEngine.ImageConversionModule.dll',
    'Assembly-CSharp.dll', '0Harmony.dll'
)
foreach ($dll in $refDlls) {
    $p = Join-Path $managed $dll
    if (-not (Test-Path $p)) { throw "缺少引用程序集: $p" }
}

$sources = @(Get-ChildItem $srcDir -Recurse -Filter *.cs | Sort-Object FullName)
if ($sources.Count -eq 0) { throw "src 下没有 .cs 文件: $srcDir" }

$sprites = @('spray_empty', 'spray_quarter', 'spray_half', 'spray_full')
Add-Type -AssemblyName System.Drawing
foreach ($key in $sprites) {
    $png = Join-Path $texDir ($key + '.png')
    if (-not (Test-Path $png)) { throw "缺少喷壶贴图: $png" }
    $img = [System.Drawing.Image]::FromFile($png)
    try {
        # 高清母版：只要够大、长宽比落在 1 格宽 × 2 格高的区间（2.0~3.2），运行时再按 gridSize 缩到 16x32
        $ratio = $img.Height / [double]$img.Width
        if ($img.Height -lt 512 -or $ratio -lt 2.0 -or $ratio -gt 3.2) {
            throw "喷壶母版要求 高度>=512 且 长宽比 2.0~3.2（1x2 格）：$png 实际 $($img.Width)x$($img.Height)"
        }
    }
    finally { $img.Dispose() }
}

$outDir = Join-Path $root ('build\' + $Version)
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$out = Join-Path $outDir 'ProbablyHardcoreLiquid.dll'

$cmd = '"' + $csc + '" /nologo /noconfig /target:library /langversion:5 /codepage:65001 /nostdlib+ /out:"' + $out + '"'
foreach ($dll in $refDlls) { $cmd += ' /r:"' + (Join-Path $managed $dll) + '"' }
foreach ($f in $sources) { $cmd += ' "' + $f.FullName + '"' }
foreach ($key in $sprites) {
    $png = Join-Path $texDir ($key + '.png')
    $cmd += ' /resource:"' + $png + '",ProbablyHardcoreLiquid.' + $key + '.png'
}

Write-Host ("编译 {0} ({1} 个源文件) ..." -f $Version, $sources.Count)
cmd /c $cmd
if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }

$manifestText = Get-Content $manifestPath -Raw -Encoding UTF8
$manifestText = [regex]::Replace($manifestText, '<ModVersion>[^<]*</ModVersion>', ('<ModVersion>' + $Version + '</ModVersion>'))
$manifestOut = Join-Path $outDir 'manifest.xml'
[System.IO.File]::WriteAllText($manifestOut, $manifestText, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "构建完成: $out" -ForegroundColor Green
Write-Host "下一步: .\test.ps1 -Version $Version ; .\deploy.ps1 -Version $Version" -ForegroundColor Gray
