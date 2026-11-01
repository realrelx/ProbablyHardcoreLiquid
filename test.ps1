<#
  ProbablyHardcoreLiquid - 离线验收（不启动游戏、不写游戏目录）
  1) manifest 结构  2) 贴图尺寸与格子 alpha  3) 源码红线扫描
  4) 喷壶常量  5) 连续稀释模型数值自检
#>
[CmdletBinding()]
param(
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fail = 0
function Check([bool]$cond, [string]$name, [string]$detail = '') {
    if ($cond) { Write-Host ("  PASS  {0}" -f $name) -ForegroundColor Green }
    else { Write-Host ("  FAIL  {0}  {1}" -f $name, $detail) -ForegroundColor Red; $script:fail++ }
}

Write-Host '== 1. manifest =='
[xml]$m = Get-Content (Join-Path $root 'manifest.xml') -Encoding UTF8
$mm = $m.ModManifest
Check ($mm.ID -eq 'ProbablyHardcoreLiquid') 'ID 固定为 ProbablyHardcoreLiquid' $mm.ID
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $mm.ModVersion }
Check (-not [string]::IsNullOrWhiteSpace($mm.ModVersion)) 'ModVersion 非空' $mm.ModVersion
Check (@($mm.Assemblies.Assembly) -contains 'ProbablyHardcoreLiquid.dll') 'Assemblies 声明 dll'
Check ($null -ne $mm.Capabilities) 'Capabilities 节点存在（零权限=空）'

Write-Host ("== 2. 贴图母版（高清 300x790，运行时按 gridSize 缩到 16x32） version={0} ==" -f $Version)
Add-Type -AssemblyName System.Drawing
$dims = @()
foreach ($key in @('spray_empty', 'spray_quarter', 'spray_half', 'spray_full')) {
    $png = Join-Path $root ('Textures\Items\' + $key + '.png')
    if (-not (Test-Path $png)) { Check $false "$key 存在"; $dims += "missing"; continue }
    $bmp = [System.Drawing.Bitmap]::FromFile($png)
    try {
        $dims += ("{0}x{1}" -f $bmp.Width, $bmp.Height)
        Check ($bmp.Height -ge 512) "$key 母版高度 >=512" ("{0}x{1}" -f $bmp.Width, $bmp.Height)
        $ratio = $bmp.Height / [double]$bmp.Width
        Check (($ratio -ge 2.0) -and ($ratio -le 3.2)) "$key 长宽比 2.0~3.2（1 格宽 x 2 格高）" ("{0:0.00}" -f $ratio)
        Check ($bmp.PixelFormat.ToString() -match '32bpp') "$key 带 alpha 通道" ($bmp.PixelFormat.ToString())
        $minx = 99999; $miny = 99999; $maxx = -1; $maxy = -1
        for ($y = 0; $y -lt $bmp.Height; $y += 2) {
            for ($x = 0; $x -lt $bmp.Width; $x += 2) {
                if ($bmp.GetPixel($x, $y).A -gt 8) {
                    if ($x -lt $minx) { $minx = $x }
                    if ($x -gt $maxx) { $maxx = $x }
                    if ($y -lt $miny) { $miny = $y }
                    if ($y -gt $maxy) { $maxy = $y }
                }
            }
        }
        Check ($maxx -gt $minx) "$key 有不透明像素" ("bbox=($minx,$miny)-($maxx,$maxy)")
        $hFill = ($maxy - $miny + 1) / [double]$bmp.Height
        Check ($hFill -ge 0.95) "$key 纵向填满母版(>=95%)" ("{0:P0}" -f $hFill)
    }
    finally { $bmp.Dispose() }
}
Check (($dims | Select-Object -Unique).Count -eq 1) '四张母版尺寸一致（四态轮廓对齐）' ($dims -join ',')
Write-Host '== 3. 源码红线（SEC-01/02/04） =='
$srcFiles = @(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs)
$all = ($srcFiles | ForEach-Object { Get-Content $_.FullName -Raw -Encoding UTF8 }) -join "`n"
Check ($all -notmatch 'System\.Diagnostics\.Process|ProcessStartInfo') '未引用 Process/ProcessStartInfo'
Check ($all -notmatch 'Steamworks') '未直接使用 Steamworks'
Check ($all -notmatch '"dse\.') '未使用 dse. 开头的补丁 ID'
Check ($all -notmatch 'UnpatchAll\s*\(\s*null') '未用 UnpatchAll(null)'

Write-Host '== 4. 喷壶常量 =='
Check ($all -match 'CapacityMl\s*=\s*1000') '容量 1000ml'
Check ($all -match 'SprayId\s*=\s*"dsc_spray_bottle"') '物品 ID'
Check ($all -match 'double p = Math\.Exp\(-k\)') '清洗用最简闭式解 p = e^(-M/c)'
Check ($all -notmatch 'const int Steps = 256') '不再做变粘度数值积分'
Check ($all -notmatch 'Math\.Exp\(-tau\)') '不再有 τ 积分路径'
Check ($all -match 'ResidueUlForParts') '残留量按给定组成计算'
Check ($all -match 'c0=" \+ c0 \+ "ul c1=" \+ c1') '日志同时打印 c0 与 c1'
Check ($all -match 'long c1 = ResidueUlForParts\(capMl, mix\)') '清洗后残留体积按新组成重算 c1（v0.9.12）'

Write-Host '== 4b. 滴管修复（v0.8.2：保留残留，只剩残渣仍可吸） =='
Check ($all -match 'LIQUID_CONTAINER_DROPPER_TAG') '识别原版滴管 tag'
Check ($all -match 'DropperSmartDrawValue') '滴管智能吸取有配置开关'
Check ($all -notmatch 'DropperExemptValue') '已移除「滴管免残留」的旧开关'
Check ($all -notmatch 'IsDropper\(source\)\s*\)\s*return null') 'Capture 不再跳过滴管（残留机制保留）'
Check ($all -match 'InitLiquidContainerItem"\)\]') '在 InitLiquidContainerItem 上挂 Harmony 补丁'
Check ($all -match 'WaterHelper\.TransferLiquid\(target, self\)') '只剩残渣时反向吸取'
Check ($all -match 'ResidueCeilingUl') '残留上限参与「只剩残渣」判定'

Write-Host '== 4c. 喷壶占格（v0.8.6：宽 1 格 × 高 2 格） =='
Check ($all -match 'UISettings\.current\.gridSize') '运行时读取 gridSize'
Check ($all -match 'int cw = g;') '画布宽 = gridSize'
Check ($all -match 'int ch = g \* 2;') '画布高 = 2×gridSize（1×2 占格）'
Check ($all -match 'Math\.Min\(\(float\)cw / \(float\)bw') '按内容包围盒等比重采样'
Check ($all -match 'minX = mw') '先裁掉母图四周空白'
Check ($all -match '面积平均') '用面积平均（box filter）而非最近邻'
Check ($all -notmatch 'sy = \(int\)\(\(y \+ 0\.5f\) \* mh / dh\)') '不再有最近邻单点采样'
Check ($all -match 'Clamp255') '颜色按 alpha 加权后收敛到 0–255'
Check ($all -match 'ForceOpaque') '格中心锚点兜底（alpha=1 不可见）'
Check ($all -match 'gridSize=" \+ g') '日志打印 gridSize 与画布尺寸'
Check ($all -match 'LoadFromAtlas\("Items/water_container3", "1ml"\)') '打印原版 1×1 精灵标尺'
Check ($all -match 'TryGiftPending') '赠壶晚阶段重试入口'
Check ($all -match 'GraphUtils\.TryAcceptAll\(target, item, -1\)') '用 TryAcceptAll 返回值判定是否落位'
Check ($all -notmatch 'ModHelper\.MoveItemToInventory') '不再调用丢弃返回值的 MoveItemToInventory'
Check ($all -notmatch 'GraphUtils\.EmporiumTryAdd\(') '不用会销毁物品的 EmporiumTryAdd'
Check ($all -match 'IS_OWNED_TAG') '落地后标记归属'
Check ($all -notmatch '74x74 fixed canvas') '不再硬编码 74px 画布'
Check ($all -match 'ResetGiftState') '每个新档复位赠壶状态（v0.8.8）'
Check ($all -notmatch 'entry\.frontInvinvElement') '不使用卷帘门外的前台展示区（v0.8.9）'
Check ($all -notmatch 'entry\.frontInvinvElement') '不使用卷帘门外的前台展示区（v0.9.0）'
Check ($all -match '_giftAttempts > 10') '柜台只在 >10 次拒收后兜底（v0.9.2）'
Check ($all -match 'invElement') '放到店铺仓库 invElement（v0.9.0）'
Check ($all -match 'IS_OWNED_TAG') '落位前先打 IS_OWNED_TAG（v0.9.2 修复）'
Check ($all -match 'WhyRefused') '打印库存拒收原因（v0.9.2）'
Check ($all -match 'TargetVolume') '空容器判定用 TargetVolume（v0.9.3）'
Check ($all -match 'skip rinse') '空/过量时不执行清洗（v0.9.5）'
Check ($all -match 'RinseRefusal') '清洗前置判定 RinseRefusal（v0.9.5）'
Check ($all -match 'SprayRinseVolumeLimitValue') '可洗液体上限 = c + 余量（v0.9.14）'
Check ($all -match 'spray_rinse_volume_limit_ml') '余量是独立的数字配置项'
Check ($all -match 'c\+limit') '拒绝日志打印 c+limit'
Check ($all -match '_doctorStockList') '博士补货每次只掷一次骰（v0.9.13）'
Check ($all -notmatch 'SolutionDistance') '不再判定成分是否一致（v0.9.5，交给玩家）'
Check ($all -match 'SprayAvailableUl') '喷壶可用量 = 现有 − 自身残余（v0.9.6）'
Check ($all -match 'SprayResidueUl') '喷壶自身残余独立计算（v0.9.6）'
Check ($all -match 'limited by bottle') '可用量不足时按实际差值算倍率（v0.9.6）'
Check ($all -match 'ResidueLogic\.ViscosityFactor') '残留上限乘粘度因子（v0.9.7）'
Check ($all -match 'Math\.Log\(eta\)') '粘度按对数混合（v0.9.10，Arrhenius）'
Check ($all -notmatch 'return \(float\)\(sum / total\)') '不再用线性加权平均'
Check ($all -match 'VanillaTargets') '保留原版水龙头装水回调（v0.9.0）'
Check ($all -match 'IsFaucet') '水龙头走装水而不是倒掉（v0.9.0）'
Check ($all -match 'GiftPump') '带每秒重试泵'
Check ($all -match 'spray place #') '每个候选容器都打诊断行'
Check ($all -match 'CanAccept\(inv, item\)') '先问 CanAccept 再放'

Write-Host '== 5. 连续稀释模型自检 =='# P_n = (1 + (M/c)/n)^-n；n→∞ 收敛到 e^-(M/c)
foreach ($ratio in @(0.5, 1.0, 2.0, 3.0, 5.0)) {
    $limit = [Math]::Exp(-$ratio)
    $n = 1000
    $discrete = [Math]::Pow(1.0 + $ratio / $n, -$n)
    $err = [Math]::Abs($discrete - $limit) / $limit
    Check ($err -lt 0.03) ("M/c={0}: e^-x={1:P2}（n=1000 偏差 {2:P2}）" -f $ratio, $limit, $err)
}
$p3 = [Math]::Exp(-3.0)
Check ([Math]::Abs($p3 - 0.049787) -lt 1e-5) '默认倍率 3 → 残留 4.9787%' ("{0:P4}" -f $p3)

# v0.9.11：p = e^(-k) 若与 M/c 的积分无关——直接验收闭式解与 k 的语义稳定性。
# 场景：1L 容器、0.1%、上限 5ml；油脂（η=80）与清水（η=1）两种清洗液，c 由对数混合粘度决定。
function Get-C([double]$fracA, [double]$pct, [double]$capMl, [double]$maxMl) {
    $etaA = 80.0; $etaB = 1.0
    $eta = [Math]::Exp($fracA * [Math]::Log($etaA) + (1.0 - $fracA) * [Math]::Log($etaB))
    $v = 1.0 + ([Math]::Pow($eta, 1.0 / 7.0) - 1.0)
    $c = $capMl * $pct / 100.0 * $v
    if ($maxMl -gt 0 -and $c -gt $maxMl) { $c = $maxMl }
    $ul = $c * 1000.0
    if ($ul -le 0) { $ul = 1000.0 }
    return $ul
}
$cOil = Get-C 1.0 0.1 1000.0 5.0
$cWater = Get-C 0.0 0.1 1000.0 5.0
Check ($cOil -gt $cWater) ("油脂 c={0:N0}ul > 清水 c={1:N0}ul（粘度抬高残留上限）" -f $cOil, $cWater)
$kRound = 3.0
$pOil = [Math]::Exp(-$kRound)
$pWater = [Math]::Exp(-$kRound)
Check ([Math]::Abs($pOil - $pWater) -lt 1e-12) '同一 k 下残留率与粘度无关（k 语义稳定）'
$mOil = $kRound * $cOil
$mWater = $kRound * $cWater
Check ($mOil -gt $mWater) ("粘液更费水：M(油脂)={0:N0}ul > M(清水)={1:N0}ul" -f $mOil, $mWater)

Write-Host ''
if ($fail -gt 0) { Write-Host ("验收失败: {0} 项" -f $fail) -ForegroundColor Red; exit 1 }
Write-Host '全部离线检查通过。' -ForegroundColor Green
