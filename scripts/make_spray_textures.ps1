<#
  把一张 2x2 四联喷壶原图切成 4 张 74x74 的透明 PNG（空/1/4/半/满）。

  用法：
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\make_spray_textures.ps1 `
      -Source 'C:\path\spray_source.png'

  参数：
    -Source      四联原图（正方形，2x2 均分）
    -OutDir      输出目录，默认 ..\Textures\Items
    -Target      输出边长，默认 74（游戏 gridSize ~36.57，74 = 2x2 格）
    -ContentH    内容（不透明部分）目标高度，默认 70 → 上下各留 2px
    -Cells       4 个格子的输出名，顺序：左上、右上、左下、右下

  要点：
    * 四张图用同一个裁剪框（四格 bbox 的并集）和同一个缩放比例，瓶身不会跳动
    * 透明像素的 RGB 是 0，直接缩会出黑边 → 用 WrapMode.TileFlipXY 的 ImageAttributes
    * 缩放后把 alpha < 8 的像素清成完全透明，避免半透明毛边
#>
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$OutDir = '',
    [int]$Target = 74,
    [int]$ContentH = 70,
    [string[]]$Cells = @('spray_empty', 'spray_quarter', 'spray_half', 'spray_full')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'Textures\Items'
}
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

function Get-AlphaBBox([System.Drawing.Bitmap]$bmp, [int]$ox, [int]$oy, [int]$w, [int]$h) {
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $bytes = New-Object byte[] ($stride * $bmp.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    }
    finally { $bmp.UnlockBits($data) }

    $minX = [int]::MaxValue; $minY = [int]::MaxValue; $maxX = -1; $maxY = -1
    for ($y = $oy; $y -lt ($oy + $h); $y++) {
        $row = $y * $stride
        for ($x = $ox; $x -lt ($ox + $w); $x++) {
            if ($bytes[$row + $x * 4 + 3] -gt 8) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    return @{ MinX = $minX; MinY = $minY; MaxX = $maxX; MaxY = $maxY }
}

$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source).Path)
try {
    if ($src.Width -ne $src.Height) { throw "原图不是正方形：$($src.Width)x$($src.Height)" }
    $cell = [int]($src.Width / 2)
    Write-Host ("原图 {0}x{1}，单格 {2}px" -f $src.Width, $src.Height, $cell)

    # 四格：左上、右上、左下、右下
    $origins = @(
        @{ X = 0; Y = 0 },
        @{ X = $cell; Y = 0 },
        @{ X = 0; Y = $cell },
        @{ X = $cell; Y = $cell }
    )

    # 统一裁剪框 = 四格 bbox 的并集（相对单格坐标）
    $ux0 = [int]::MaxValue; $uy0 = [int]::MaxValue; $ux1 = -1; $uy1 = -1
    foreach ($o in $origins) {
        $bb = Get-AlphaBBox $src $o.X $o.Y $cell $cell
        if ($bb.MaxX -lt 0) { throw "有一格是全透明的（没有内容）" }
        if (($bb.MinX - $o.X) -lt $ux0) { $ux0 = $bb.MinX - $o.X }
        if (($bb.MinY - $o.Y) -lt $uy0) { $uy0 = $bb.MinY - $o.Y }
        if (($bb.MaxX - $o.X) -gt $ux1) { $ux1 = $bb.MaxX - $o.X }
        if (($bb.MaxY - $o.Y) -gt $uy1) { $uy1 = $bb.MaxY - $o.Y }
    }
    $cropW = $ux1 - $ux0 + 1
    $cropH = $uy1 - $uy0 + 1
    $scale = $ContentH / [double]$cropH
    $outW = [int][Math]::Round($cropW * $scale)
    $outH = $ContentH
    if ($outW -gt $Target) { throw "内容太宽：缩到 $outW px 超过画布 $Target px" }
    Write-Host ("统一裁剪框 {0}x{1}（格内偏移 {2},{3}）→ 缩放 {4:P1} → {5}x{6}" -f $cropW, $cropH, $ux0, $uy0, $scale, $outW, $outH)

    $destX = [int][Math]::Floor(($Target - $outW) / 2.0)
    $destY = [int][Math]::Floor(($Target - $outH) / 2.0)

    # 缩小时避免边缘黑边：TileFlipXY
    $ia = New-Object System.Drawing.Imaging.ImageAttributes
    $ia.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)

    for ($i = 0; $i -lt 4; $i++) {
        $o = $origins[$i]
        $cropRect = New-Object System.Drawing.Rectangle ($o.X + $ux0), ($o.Y + $uy0), $cropW, $cropH
        $destRect = New-Object System.Drawing.Rectangle $destX, $destY, $outW, $outH

        $out = New-Object System.Drawing.Bitmap $Target, $Target, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($out)
        try {
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.DrawImage($src, $destRect, $cropRect.X, $cropRect.Y, $cropRect.Width, $cropRect.Height,
                [System.Drawing.GraphicsUnit]::Pixel, $ia)
        }
        finally { $g.Dispose() }

        # 清掉缩放产生的半透明毛边
        $lr = New-Object System.Drawing.Rectangle 0, 0, $Target, $Target
        $d = $out.LockBits($lr, [System.Drawing.Imaging.ImageLockMode]::ReadWrite, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $buf = New-Object byte[] ($d.Stride * $Target)
            [System.Runtime.InteropServices.Marshal]::Copy($d.Scan0, $buf, 0, $buf.Length)
            for ($y = 0; $y -lt $Target; $y++) {
                for ($x = 0; $x -lt $Target; $x++) {
                    $idx = $y * $d.Stride + $x * 4 + 3
                    if ($buf[$idx] -lt 8) { $buf[$idx] = 0 }
                }
            }
            [System.Runtime.InteropServices.Marshal]::Copy($buf, 0, $d.Scan0, $buf.Length)
        }
        finally { $out.UnlockBits($d) }

        $path = Join-Path $OutDir ($Cells[$i] + '.png')
        $out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $out.Dispose()
        Write-Host ("写出 {0}" -f $path)
    }
}
finally {
    $src.Dispose()
}

Write-Host '四张贴图已生成，74x74 / 透明背景。' -ForegroundColor Green
