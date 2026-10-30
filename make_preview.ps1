# Generates preview.png (512x512 pixel art) for the Probably Hardcore Liquid mod.
Add-Type -AssemblyName System.Drawing

$W = 64
$m = New-Object 'char[,]' $W, $W
for ($y = 0; $y -lt $W; $y++) { for ($x = 0; $x -lt $W; $x++) { $m[$x, $y] = '.' } }

# scattered stars
$stars = @(@(5,2),@(58,3),@(30,4),@(15,5),@(48,6),@(35,7),@(60,8),@(3,9),@(55,10),@(26,7),@(40,3),@(62,15),@(2,20),@(58,30),@(4,36),@(60,44))
foreach ($s in $stars) { if ($s[0] -lt $W -and $s[1] -lt $W) { $m[$s[0], $s[1]] = '*' } }

function Rect($x0, $y0, $x1, $y1, $c) { for ($y = $y0; $y -le $y1; $y++) { for ($x = $x0; $x -le $x1; $x++) { $m[$x, $y] = $c } } }

# ---- bottle (upright, empty, residue at bottom) ----
Rect 11 8 20 8 '#'                     # cap top
Rect 11 9 20 12 'C'                    # cap body
$m[11,9]='#'; $m[20,9]='#'; $m[11,12]='#'; $m[20,12]='#'
Rect 11 13 20 14 'B'                   # neck
$m[11,13]='#'; $m[20,13]='#'; $m[11,14]='#'; $m[20,14]='#'
Rect 10 15 21 15 'B'; $m[10,15]='#'; $m[21,15]='#'
Rect 9 16 22 17 'B'; $m[9,16]='#'; $m[22,16]='#'; $m[9,17]='#'; $m[22,17]='#'
for ($y = 18; $y -le 39; $y++) { $m[9,$y]='#'; $m[22,$y]='#'; $m[10,$y]='B'; $m[21,$y]='B' }
Rect 10 40 21 40 'Y'                   # liquid surface glow
$m[9,40]='#'; $m[22,40]='#'
Rect 10 41 21 43 'R'                   # the stubborn residue
$m[9,41]='#'; $m[22,41]='#'; $m[9,42]='#'; $m[22,42]='#'; $m[9,43]='#'; $m[22,43]='#'
$m[13,41]='W'; $m[16,41]='W'; $m[18,42]='W'   # glints
Rect 9 44 22 44 '#'                    # base

# ---- 3x5 pixel font ----
$glyphs = @{
  'H'=@('#.##','#.##','#####','#.##','#.##')
  'A'=@('.###','#..#','#####','#..#','#..#')
  'R'=@('####','#..#','####','#.#.','#..#')
  'D'=@('####','#..#','#..#','#..#','####')
  'C'=@('.###','#...','#...','#...','.###')
  'O'=@('.###','#..#','#..#','#..#','.###')
  'E'=@('###.','#...','###.','#...','###.')
  'L'=@('#...','#...','#...','#...','###.')
  'I'=@('#','#','#','#','#')
  'Q'=@('.###','#..#','#.##','#..#','.###')
  'U'=@('#..#','#..#','#..#','#..#','.###')
  'S'=@('###.','#...','###.','...#','###.')
  'T'=@('###.','.#..','.#..','.#..','.#..')
  'K'=@('#..#','#.#.','##..','#.#.','#..#')
  'Y'=@('#.#.','#.#.','.#..','.#..','.#..')
}
function StampText($text, $startX, $startY) {
  $x = $startX
  foreach ($ch in $text.ToCharArray()) {
    if ($ch -eq ' ') { $x += 2; continue }
    $g = $glyphs[[string]$ch]
    $gw = $g[0].Length
    for ($r = 0; $r -lt 5; $r++) {
      $line = $g[$r]
      for ($c = 0; $c -lt $line.Length; $c++) {
        $px = $x + $c; $py = $startY + $r
        if ($px -lt $W -and $py -lt $W) { $m[$px, $py] = $line[$c] }
      }
    }
    $x += $gw + 1
  }
}
StampText 'HARDCORE LIQUID' 6 47
StampText 'STICKY RESIDUE' 8 53

# ---- palette & render ----
$pal = @{
  '.' = '#10142b'; '*' = '#2b3155'; '#' = '#1e3a5f'; 'B' = '#63b8e8';
  'C' = '#dfe7f5'; 'R' = '#2f96e8'; 'W' = '#ffffff'; 'Y' = '#ffd84d'
}
$bmp = New-Object System.Drawing.Bitmap $W, $W
for ($y = 0; $y -lt $W; $y++) {
  for ($x = 0; $x -lt $W; $x++) {
    $c = [string]$m[$x, $y]
    $hex = $pal[$c]
    if ($hex -eq $null) { $hex = '#ff00ff' }
    $bmp.SetPixel($x, $y, [System.Drawing.ColorTranslator]::FromHtml($hex))
  }
}
$big = New-Object System.Drawing.Bitmap 512, 512
$g = [System.Drawing.Graphics]::FromImage($big)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($bmp, 0, 0, 512, 512)
$out = 'C:\Users\han\Documents\deepseek-harness\default-workspace\ProbablyHardcoreLiquid\preview.png'
$big.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $big.Dispose()
Write-Output "saved: $out"
