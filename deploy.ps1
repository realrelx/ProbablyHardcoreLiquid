<#
  ProbablyHardcoreLiquid - 安装到本地游戏 Mods 目录。
  只在显式调用时写游戏目录（框架要求：构建不自动落盘到游戏）。
  游戏目录来自 -GamePath 或环境变量 PROBABLY_STOLEN_PATH，不硬编码。
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$GamePath = '',
    [string]$ModFolder = 'ProbablyHardcoreLiquid'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($GamePath)) { $GamePath = $env:PROBABLY_STOLEN_PATH }
if ([string]::IsNullOrWhiteSpace($GamePath)) {
    throw '请用 -GamePath 或环境变量 PROBABLY_STOLEN_PATH 指定游戏安装目录，例如：' +
          "`n  .\deploy.ps1 -GamePath 'D:\steam\steamapps\common\Probably Stolen'"
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$m = Get-Content (Join-Path $root 'manifest.xml') -Encoding UTF8
    $Version = $m.ModManifest.ModVersion
}

$buildDir = Join-Path $root ('build\' + $Version)
if (-not (Test-Path $buildDir)) { throw "没有构建产物，先跑 build.ps1: $buildDir" }
$dll = Join-Path $buildDir 'ProbablyHardcoreLiquid.dll'
$mf = Join-Path $buildDir 'manifest.xml'
foreach ($f in @($dll, $mf)) { if (-not (Test-Path $f)) { throw "缺少文件: $f" } }

if (Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue) {
    throw '游戏正在运行，请先退出游戏（DLL 被占用会导致覆盖失败）'
}

$modDir = Join-Path $GamePath ('Mods\' + $ModFolder)
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
Copy-Item $dll $modDir -Force
Copy-Item $mf $modDir -Force

Write-Host "已安装到: $modDir" -ForegroundColor Green
Write-Host '进游戏启用 MOD，然后看 Player.log 里的 [HardcoreLiquid] 行。' -ForegroundColor Gray
