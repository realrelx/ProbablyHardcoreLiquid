# 状态说明 · ProbablyHardcoreLiquid（框架工程版）

- 工程路径：`何小鲁框架正式版本/projects/ProbablyHardcoreLiquid/`
- 版本：**0.8.3**（框架派生版；原开发版为 `ProbablyHardcoreLiquid_工程` v0.7.3）
- 阶段：**离线可构建、离线验收 54 项全绿；游戏内行为待实机**

## v0.8.3（2026-11-01）

- 实测日志定位：`gridSize=16`，画布 `32×32`，`size=4` 说明形状已是 2×2。
- 但存档 `save_44.es3` 里**查不到 `dsc_spray_bottle`** → 物品根本没进存档。
- 根因：`ModHelper.MoveItemToInventory(item)` 的 IL 就是
  `GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement, item, -1)` 且**丢弃返回值**；
  在 `OnGameLoadedInit` 时刻 `EmporiumEntry.Instance` / `invElement` 还没建好 → 静默空操作，物品变孤儿。
- 改法：物品只造一次存在 `_pendingItem`；`OnGameLoadedLate` / `OnShutterOpenedEarly` 重试；
  用 `TryAcceptAll` 返回值（申请量 − 实转移）判定，`0` 才算落位，否则留着下次再来；
  **不再调用** `GraphUtils.EmporiumTryAdd`（放不下时会 `DestroyIfUnplaced` 直接销毁物品）。
- 新增诊断：`ref sprite: vanilla 1x1 dropper = WxH px, gridSize=… → 2x2 should be …`。

## 已完成

| 项目 | 状态 | 证据等级 |
|---|---|---|
| 按框架目录重构（src/Core.cs、Items、Mechanics、Infrastructure） | 完成 | 本机文件核对 |
| 残留系统（ResidueLogic + 10 个液体出口补丁） | 完成（沿用 v0.7.3 逻辑） | 原工程已实机验证 |
| 喷壶物品（注册、2×2、1000ml、80 信用点、TOOL） | 完成 | 离线编译 + 常量断言 |
| 喷壶清洗改为连续稀释闭式解 `p = e^(-M/c)` | 完成（v0.8.0 改写） | 离线编译 + 数学自检 |
| **占格改为运行时 2g×2g + 四格中心锚点** | 完成（v0.8.2） | IL 实证 + 源码断言 |
| **开局赠壶进背包并标记 `IS_OWNED_TAG`** | 完成（v0.8.2） | 源码断言，待实机 |
| **滴管「只剩残渣仍可吸」（保留残留）** | 完成（v0.8.2，撤销 v0.8.1 的免残留） | IL 实证 + 源码断言 |
| 化学家开局礼 / 商人概率卖空壶 | 完成（沿用） | 原工程已实机验证（礼装触发 diag 通过） |
| 贴图 4 态（四联原图切片，74px 母图运行时重采样） | 完成 | 离线像素断言 |
| build / test / deploy 三段脚本 | 完成 | 本机跑通 |
| 贴图切片脚本 `scripts/make_spray_textures.ps1` | 完成 | 本机跑通（1254→4×74） |
| 离线验收 51 项 | **全部通过** | 本机执行输出 |

## 待验证（必须进游戏）

1. 喷壶在背包/仓库里**占 2×2**，且能放进仓库任意 2×2 空位（v0.8.2 首要验收点）。
2. 开局赠壶落在**背包**而不是柜台；能拖动、能点。
3. 喷壶拖到液体容器 → 目标残留是否按 e^(-M/c) 落到 `c` 体积。
4. 清洗后目标液体组成是否 = `p·A + (1-p)·B`。
5. 喷壶倒排水口/水槽只倒掉、不转移到别的容器。
6. 液位贴图 4 态切换（<2% 空、<35% 1/4、<70% 半、否则满）。
7. **滴管**：吸满 → 倒入另一容器 → 管内只剩残渣 → 再点回原容器仍能吸；且残渣确实还在（查 tooltip 体积）。
8. 与 `DeepSpaceChemistry` 同时启用时是否互相干扰。

## 日志对照（排障用）

| 日志 | 含义 |
|---|---|
| `spray sprites loaded: 4/4 (gridSize=16, canvas=32x32, anchorFixes=…)` | 画布按真实 gridSize 生成 |
| `ref sprite: vanilla 1x1 dropper = 16x16 px → 2x2 should be 32x32` | 原版 1×1 精灵标尺 |
| `spray gift placed into inventory, size=4 (attempt 2)` | 第 N 次尝试才真正放进库存 |
| `spray gift deferred #1: EmporiumEntry.Instance=null` | 太早，等下一个钩子 |
| `spray gift deferred #1: inventory has no room (remain=4)` | 库存满，物品保留未销毁 |
| `gridSize unavailable yet; spray sprites deferred` | 调用太早，进游戏后自动重算 |
| `dropper smart-draw: 5 -> 20 ul (ceiling 25)` | 滴管走吸取分支 |
| `dropper draw skipped (target empty)` | 目标为空，吃掉交互避免倒残渣 |

## 已知风险

- R1 `gridSize` 在运行时读取，改分辨率/UI 缩放后已生成的贴图不会自动重做（重启即可）。
- R2 `ForceOpaque` 借同格像素；整格透明时填 `#DDE8EC`，极细瓶嘴可能被补 1 像素。
- R3 清洗时 `WaterHelper.Remove` + `AddLiquid` 会触发我们自己的 10 个补丁，靠 `Spray.Rinsing` 短路；新增补丁必须同样判 `Rinsing`。
- R4 `spray_rinse_ratio` 是模型常数不是物理常数（默认 3 → 残留 4.98%）。
- R5 滴管修补包的是 `onThisTargetItemFunc`；若官方以后在 `InitLiquidContainerItem` 之外再叠加交互委托，需要重新确认包装顺序。
- R6 若仓库放置判定从"中心像素"改成"整格覆盖率"，锚点法要重写。

## 下一步

```powershell
.\build.ps1                                     # 产物 build\0.8.2\ProbablyHardcoreLiquid.dll
.\test.ps1                                      # 51 项离线检查
.\deploy.ps1                                    # 写入 D:\steam\...\Mods\ProbablyHardcoreLiquid（需 danger-full-access）
.\scripts\make_spray_textures.ps1 -Source <四联原图>   # 重新切贴图（可选）
```

## v0.8.5（2026-11-01）

- 贴图重采样：\BuildCanvas\ 由**最近邻**改为**面积平均（box filter）**，颜色按 alpha 加权；74px 母图 → 2g×2g 的 2.3 倍下采样不再切出锯齿。
- 占格锚点：四格中心仍强制 alpha>0，但 alpha 从 255 改为 **1/255**——形状判定只看 alpha>0，屏幕上看不见，不再出现"瓶旁边飘四个小点"。
- 离线检查：\	est.ps1\ 全量通过（4c 新增"面积平均/不再单点采样/Clamp255"三条）。
- 部署：\uild\0.8.5\ProbablyHardcoreLiquid.dll\ 76,288 B，SHA256 \452C65876B79E4291F4F7F128313EB5260D526B013797DBD001E07C86D716A0B\，已装入 \Mods\ProbablyHardcoreLiquid\。
- 待实机：确认日志 \spray sprites loaded: 4/4 (gridSize=16, canvas=32x32, anchorFixes=16)\ 且占格仍为 2×2、无可见杂点。
## v0.8.6（2026-11-01）

- **占格 2×2 → 1×2**（宽 1 格、高 2 格）：瓶子本来就是竖长的（母图内容 250×579 ≈ 1:2.3），16×32 画布比 32×32 更贴合，像素都花在瓶子上，同时省一半仓库格子。
- BuildCanvas(master, cw, ch, out fixes)：① 先裁母图不透明包围盒（去掉正方形母图两侧空白）→ ② 面积平均等比缩放到 cw×ch，宽度差 ≤25% 时横向补满 → ③ 两格中心锚点 alpha=1（不可见）兜底。
- EnsureSprites 里 cw = gridSize、ch = 2 * gridSize；新增 _canvasHeight；日志改为 canvas=16x32。
- 离线检查：全量通过（4c 改为 1×2 语义，新增"画布宽 = gridSize""画布高 = 2×gridSize""裁包围盒"三条）。
- 部署：uild\0.8.6\ProbablyHardcoreLiquid.dll 76,800 B，SHA256 A0E4BD252D0A6CB64E9E9AAFEED23897C179B16604F4196E5E3AEC141025BCF8。
- 待实机：日志应为 spray sprites loaded: 4/4 (gridSize=16, canvas=16x32, anchorFixes=0)，占格 1 宽 2 高。
## v0.8.7（2026-11-01）

- 落位诊断：每个候选容器打 spray place #N <名字>: canAccept=… maySlot=… insertLocked=… supportsGroup=… children=… size=… units=… parent=…。
- 一次性实测结论（Player.log）：背包 invElement = canAccept=False maySlot=False children=17；selectedBackpack=null；ackInvinvElement = canAccept=True → 喷壶被放到后台柜台而不是存储空间。
- 新增 GiftPump（MonoBehaviour，每秒重试，最长 10 分钟）。

## v0.8.8（2026-11-01）

- **每个新档都发壶**：_giftGiven/_pendingItem/_giftAttempts 是进程级静态字段，同一会话连开新档时第二个档起被旧标记挡住；Core.OnNewGameStarted 现在先调 Spray.ResetGiftState()。
- **两段式落位**：新造物品的 modifiedShape 为 null，背包槽位搜索因此失败（柜台不校验这项）。现在先放进柜台把形状状态坐实，紧接着立刻再 TryAcceptAll(invElement, …)（TryAcceptOnce 内部先 Expel 旧容器）；成功才算完成，失败就留在柜台并继续每秒重试。
- GameItem.shape / modifiedShape 是 GridShape（ref struct），C#5 编译器无法引用（CS0570），所以只能靠两段式间接坐实，不能直接赋值。
- 离线检查 59 项通过；uild\0.8.8\ProbablyHardcoreLiquid.dll 79,872 B，SHA256 6FEFF7134B6F8087CA2D44A8757F4D44AD2DF21E2C41B4ACE39C5EB12446DC17，已部署。
## v0.8.9（2026-11-01）

- **不再使用 rontInvinvElement**：实测那是卷帘门外的展示区，物品进去后还不能拖动。用户 m01489 现场反馈"放在开卷帘门的外面、不让我挪动"就是它。
- 落位顺序改为：invElement（玩家随身背包）→ ackInvinvElement（店铺仓库/后台）。两边都不收就保留物品、每秒重试（最长 10 分钟），绝不丢到门外。
- 日志改为一行汇总：spray place #N invElement: <desc> / spray gift deferred #N: invElement(remain=…:<desc>); backInvinvElement(remain=…:<desc>);。
- 离线检查 60 项通过；uild\0.8.9\ProbablyHardcoreLiquid.dll 79,360 B，SHA256 D54356C36CD9E9877F2E06BC3E2D7349AD37509FC7F2A34584015CD7526DA8F8，已部署。
## v0.9.0（2026-11-01）

- **落位目标改为店铺仓库 invElement**（SetBigInv 28×10 / SetSmallInv 16~20×10 的大网格）。ackInvinvElement 是谈判柜台（v0.8.9 放错，用户 m01554 反馈），rontInvinvElement 是卷帘门外展示区，两者都不再用。
- 落位前先 item.ValidateShapeState() + GameInventory.Validate()，再 GraphUtils.TryAcceptAll(invElement, item, -1)；emain>0 就保留物品并每秒重试，日志 spray gift deferred #N: invElement(remain=…:modSize=…:…)。
- **修水龙头**：WaterHelper.InitLiquidContainerItem 内部会挂 onThisTargetItemFunc（负责"拖到水龙头装水 / 拖到排水口倒掉"），v0.8.x 直接把它覆盖成 SprayTargetAction，导致拖水龙头毫无反应。现在创建时先用 ConditionalWeakTable<GameItem, Action<GameItem,GameItem>> VanillaTargets 存下原委托，SprayTargetAction 里遇到 IsFaucet 就把交互交回原版（保留当日水质/净水器/升级判定），IsDrain 走 EmptyContainer，液体容器走 Rinse。
- 离线检查 63 项通过；uild\0.9.0\ProbablyHardcoreLiquid.dll 79,872 B，SHA256 B5749085EB950E92A944CA3FE76193D6D7988C1306DBF665446B7403E50242DF。
- **部署被阻塞**：deploy.ps1 报"游戏正在运行，请先退出游戏"（DLL 被占用），需要关游戏后重新部署。
- 新增 docs\贴图要求_高清版.md：给 GPT 的 512×1024 四态母版规格 + 英文提示词 + 验收脚本规则。
## v0.9.1（2026-11-01）高清母版替换

- 用户给的 2×2 图是 **24bpp 无 alpha**，棋盘格是画上去的，不是透明通道。用"中性亮色"判据（max-min≤4 且亮度≥228）抠掉背景——玻璃是偏蓝的（B−R≈21），不会被误伤；实测无白边残留。
- AI 出图四格位置/大小不一致（x=99 与 141、h=776 与 783），所以按**每格自己的 bbox** 裁切，再统一缩放到 **300×790** 画布，四态轮廓对齐。
- 母版：Textures\Items\spray_*.png 300×790（约 197 KB 一张），原图存档 Textures\Source\spray_hd_sheet_2x2.png；整表切图脚本 ..\..\..\make_spray_hd.ps1。
- BuildCanvas 横向补满阈值 1.25 → **1.45**（母版宽高比 0.38，比 16:32=0.5 窄，补满更清楚）。
- uild.ps1 的贴图校验从"必须 74×74"改为"高度≥512 且长宽比 2.0~3.2"；	est.ps1 第 2 节改成母版规则（≥512 高、比例、alpha、纵向填充≥95%、四张尺寸一致）。
- 部署  .9.1：DLL 811,520 B（内嵌 4 张高清母版），SHA256 3CB94491FD001B24A4AA208B044208D880BBBEFD81BE51CD8BED13A9FFE229C1。
## 回滚记录（2026-11-01 17:2x）

- 用户要求"回调到 0.89 店铺仓库 + 水龙头修复"版本。版本号核对：**0.8.9 没有水龙头修复**（无 VanillaTargets），同时也没有 ValidateShapeState、落位还会退回 backInv/frontInv（谈判柜台/卷帘门外）。
- 与用户描述相符的是 **0.9.0**（店铺仓库 invElement 唯一落点 + 水龙头 VanillaTargets 回调）。已回滚部署 0.9.0：DLL 79,872 B，SHA256 B5749085EB950E92A944CA3FE76193D6D7988C1306DBF665446B7403E50242DF。
- 0.9.1（高清母版）构建物仍保留在 uild\0.9.1\，随时可 deploy.ps1 -Version 0.9.1 重新装回。
- **已发现的问题（0.9.x 共同）**：invElement 一直 canAccept=False maySlot=False，掉落到 deferred 重试泵最多 22 次仍未落位。下一步假设：IS_OWNED_TAG 在落位成功后才设置，而库存可能只收已归属物品 → 改为落位**前**先 EnableTag("IS_OWNED_TAG", true) 再试。
## v0.9.2（2026-11-01）仓库落位修复

- 线索：日志里 invElement 连拒 22 次（maySlot=False），而 0.8.x 放到 ackInvinvElement 时是 canAccept=True。用户也说过"喷壶只有交互过一次才能放进存储空间"。
- 推论：**店铺仓库只收已归属物品**（IS_OWNED_TAG），而 0.8~0.9 一直是"落位成功后才 EnableTag"，所以仓库永远拒收 —— 死循环。
- 改动：TryPlacePending 在落位**前**先 item.EnableTag("IS_OWNED_TAG", true)；新增 WhyRefused() 打印 mayInventoryAddItemFunc（每个库存自己的准入委托）的返回值与 overrideLockInsert；拒收超过 10 次才退到 ackInvinvElement（店内柜台，可拖动），**绝不**用 rontInvinvElement（卷帘门外）。
- manifest.xml → 0.9.2；	est.ps1 断言同步（柜台仅作兜底、IS_OWNED_TAG、WhyRefused），全过。
- 产物 uild\0.9.2\ProbablyHardcoreLiquid.dll 812,544 B，SHA256 5847351C14DD8738…，已部署。
- 待验证日志：spray gift deferred #1: invElement(... mayAddItem=False ...) 若出现 mayAddItem=False 即坐实"准入委托拒收"；若直接 spray gift placed into invElement 则修复成功。
## v0.9.3（2026-11-01）空容器不响应

- 需求（用户）：喷壶对**空容器**不做任何操作，只有里面有东西才清洗。
- CanSprayTarget：液体容器分支改成先判 WaterHelper.CanUseWaterContainer，再判 TargetVolume(target) > 0；为空则返回 false（鼠标移上去没有可用交互）。
- SprayTargetAction：容器分支开头再兜一层 if (TargetVolume(target) <= 0) { 打日志 + return; }，不消耗喷壶里的水、也不刷新贴图。
- 新增 private static int TargetVolume(GameItem) 包 WaterHelper.GetTotalVolume，异常一律返回 0。
- 说明：只留残留（体积极小但 >0）的容器仍会被清洗——残留本身也算"里面有东西"。
- manifest.xml → 0.9.3；	est.ps1 加 2 条断言，全过；产物 813,056 B，SHA256 ECDC4A548A4F8F47…，已部署。
## v0.9.4（2026-11-01）清洗前置判定（三种情形不洗）

需求（用户 m01834）：① 没液体不洗；② 液体成分与清洗液一致（允许一点偏差）不洗；③ 其他情形只要有残余就洗；④ 液体量 > 残留上限也不洗。

- 新增 RinseRefusal(spray, target, out targetVol)，返回 null 才允许清洗，否则返回原因：
  - empty —— TargetVolume(target) <= 0
  - liquid Nul > residue cap Cul —— 液体量超过残留上限 TargetResidueUl(target)
  - spray is empty —— 喷壶里没水
  - lready same solution (dist=… <= tol) —— 成分距离 ≤ 阈值
- 新增 SolutionDistance(a,b)：各成分归一化占比的 **L1 距离**（0 = 完全一致，2 = 完全不相交）。
- 新增 SprayVolume(spray)；CanSprayTarget 与 SprayTargetAction 都改走 RinseRefusal，后者打 spray: skip rinse (<原因>)。
- 新增配置 spray_same_solution_tolerance（float，默认 **0.15**，组 spray）：容器液体与清洗液的成分距离小于该值即视为同一种溶液，不清洗。
- Config.Define 的 float 重载签名是 (key, default, group, name, null, null, tooltip, false) —— 少了那两个 null 会报 CS1660（委托转 float?）。
- manifest.xml → 0.9.4；	est.ps1 加 4 条断言、改 1 条，全过。
- 产物 815,104 B，SHA256  D827FC52C56205F…，已部署。
## v0.9.5（2026-11-01）去掉"成分一致"判定

- 用户意见（m01874）："成分一致不一致感觉没必要判定，让玩家自行决定吧，不搞复杂逻辑"。
- 删除 SolutionDistance()、SpraySameSolutionTol 配置项与 SpraySameSolutionTolValue 访问器（同时去掉设置里 spray_same_solution_tolerance 那一行）。
- RinseRefusal 现在只剩三条：empty / liquid Nul > residue cap Cul / spray is empty。
- 	est.ps1：删掉 SolutionDistance 与 already same solution 两条断言，新增"不再判定成分是否一致"反向断言；全过。
- manifest.xml → 0.9.5；产物 813,568 B，SHA256 5E821A19C0FF5B82…，已部署。
## v0.9.6（2026-11-01）倍率按喷壶可用量封顶 + n 支持小数

- 澄清：TargetResidueUl() **不含粘度因子**，只是 min(容量ml × 残留比例%, MaxMlValue) × 1000；粘度只作用在 ResidueLogic.ComputeResidueUl()（真实转移后实际留下多少残留），两者不要混。
- 新增 SprayResidueUl(spray)（喷壶自身残余，同款公式，容量 1000ml）与 SprayAvailableUl(spray) = SprayVolume − SprayResidueUl（倒不出去的那部分喷不出去）。
- Rinse：k 不再用 sprayVol/c 封顶，改为 vail/c：
  double maxK = (double)availUl / (double)cUl; if (k > maxK) { k = maxK; 打 limited by bottle 日志 }
  → 壶里只剩一点时，倍率变成小数（连续），不再是整数跳变。
- RinseRefusal：spray is empty 改为 spray holds only its own residue (Nul <= Cul)，同样是"没有可喷出的量"。
- 配置 spray_rinse_ratio 加 min/max：
ew System.Nullable<float>(0.25f), new System.Nullable<float>(20f)（原来两个 null），允许 0.25~20 的离散值，例如 2.5。
- Config.Define float 重载：(key, default, section, label, float? min, float? max, tooltip, requiresRestart)；Nullable<float> 参数写成 
ew System.Nullable<float>(x) 最稳（3f as float? 在 C#5 里不成立）。
- manifest.xml → 0.9.6；	est.ps1 加 3 条断言，全过；产物 814,080 B，SHA256 D56F822396137307…，已部署。
## v0.9.7（2026-11-01）残留上限纳入粘度

- 用户要求：判定用的 c 要和残留结算用同一套口径（带粘度）。
- ResidueLogic：MixViscosity 由 private 改 internal；新增 public static double ViscosityFactor(int[] parts)（1 + (η^(1/7) − 1) × viscosity_scale，下限 0.05），ComputeResidueUl 改为直接调用它，避免两处公式漂移。
- Spray.TargetResidueUl / SprayResidueUl 的 cMl 都乘 ResidueLogic.ViscosityFactor(GetLiquidParts(...))：
  c = min(容量ml × 比例% × 粘度因子, MaxMlValue) × 1000
  → 粘稠液体倒不干净，残留上限更大，更容易被判成"内容物"而不清洗；和结算完全一致。
- 顺序：先按目标**当前**内容物算粘度，再 MaxUlValue 封顶（与 ComputeResidueUl 一致）。
- manifest.xml → 0.9.7；	est.ps1 加 1 条断言，全过；产物 814,592 B，SHA256 74D22E87FCEBA6E7…，已部署。
## v0.9.10（2026-11-01）混合粘度改为对数混合

- ResidueLogic.MixViscosity 从线性体积平均改为**对数混合**：ln η_mix = Σ(xᵢ·ln ηᵢ)，即几何平均；η<0.01 的组分按 0.01 处理（防 log(0)）。
- 整数版简化为"转 double 后调用浮点版"，两处不可能再漂移。
- 影响：混合物的残留普遍下降——水 90% + 油脂 10% 从 1.37 ml 降到 1.06 ml；水 50% + 碱液 50% 从 1.32 → 1.20。
- manifest.xml → 0.9.10；	est.ps1 加 2 条断言并把变粘度积分参考解同步改为对数混合（τ=4.869 vs 参考 4.876，偏差 0.14%）；93 项全过。
- 部署 815,616 B，SHA256 4CAE023374ABAA80…。
## v0.9.11（2026-11-01）清洗模型回到最简版

- 用户决定：清洗过程中不考虑粘度变化与残余体积变化，回到老的连续稀释模型；粘度/残留只用来算 c。
- Rinse() 删除 256 步变粘度积分，改回 double p = Math.Exp(-k);（k = M/c，等价于 e^(−M/c)）。
- 清洗后残留体积 long c1 = c0;（体积在清洗过程中不变），不再按新组成重算。
- 日志改为 spray rinse: target=… c=…ul sprayed=…ul (M/c=…, want …) retained=…%。
- 	est.ps1 第 4 节改成"最简闭式解"断言（无 Steps = 256、无 Math.Exp(-tau)、有 long c1 = c0），第 5 节改成 k 语义稳定性 + 粘度只影响水费的两条数值断言。**93 项全过**。
- 产物 uild\0.9.11\ProbablyHardcoreLiquid.dll 815,104 B，SHA256 C0D46EB6B0D017C4…，已部署（manifest 0.9.11）。
## v0.9.12（2026-11-01）清洗后残留体积按新组成重算

- 用户要求：清洗**过程**用常数 c（闭式解 p = e^(−k) 不变），但洗完之后容器里剩下多少，要按**洗后新组成**重算。
- Rinse()：long c1 = ResidueUlForParts(capMl, mix);（v0.9.11 曾写死 c1 = c0，现改回按 mix 计算）。
- 含义：洗成更粘的混合物，那点残渣就更难倒干净（c1 > c0）；洗成更稀的，残留反而更少。
- 日志恢复 c0=…ul c1=…ul 双打印，便于对照。
- 	est.ps1 相应断言改为 long c1 = ResidueUlForParts(capMl, mix)。**93 项全过**。
- 产物 uild\0.9.12\ProbablyHardcoreLiquid.dll 815,104 B，SHA256 2EDA49D2BC6C4973…，已部署（manifest 0.9.12）。
## v0.9.13（2026-11-01）配置控件 / 博士概率 / 容器可洗范围

1. **清洗倍率改成数字输入框**：spray_rinse_ratio 的 Define 从 min=0.25, max=20 改回 
ull, null
   —— ModConfigWindow.BuildEntry 的判定是"数字类型 **且有 Range** → AddSlider，否则 → 输入框"，
   所以只要传 min/max 就一定是滑块。给 null 才是可填小数的输入框。
2. **博士概率修正**：OnPlaceInventorInventoryItemEarly 会随每件货品各调一次，逐一掷骰会把 0.25 放大成
   1-(1-0.25)^6 ≈ 82%（等于用户看到的"100%"）。改为：同一个 list 实例 / 3 秒内只掷一次，货架上已有喷壶不再补。
   默认 0.35 → **0.25**；玩家配置里的 spray_doctor_prob 也从 1 改回 0.25（备份 ProbablyHardcoreLiquid.json.bak_0913）。
3. **容器可洗范围**：删掉 	argetVol > c 的拒绝（c = 容量×0.1% 只有 1ml，导致几乎所有容器都点不动），
   现在任意非空容器都能洗；清洗等于"倒空 → 回填 c"，日志新增 dumped=…ul 记录倒掉了多少。
- 	est.ps1：esidue cap 断言换成 	argetVol > c 不存在 + _doctorStockList 存在。**94 项全过**。
- 产物 uild\0.9.13\ProbablyHardcoreLiquid.dll 815,616 B，SHA256 BB3CF6BDC5A361A9…，已部署（manifest 0.9.13）。
## v0.9.14（2026-11-01）可洗液体上限 = c + 余量

- 新增数字配置项 spray_rinse_volume_limit_ml（默认 5，单位 ml，输入框非滑块），访问器 SprayRinseVolumeLimitValue。
- RinseRefusal()：long cap = c + (long)(limitMl * 1000f); if (targetVol > cap) return "liquid ... > c+limit ...";，limitMl <= 0 视为不限制。
- 语义：容器液体 > 「该容器实际残留上限 c + 余量」才拒绝清洗。例：1L 容器 c=1ml、余量 5ml → 超过 6ml 不洗；若残留上限被 max_residue_ml 封到 5ml，则超过 10ml 不洗。
- 拒绝日志：liquid 12345ul > c+limit 10000ul (c=5000ul + 5ml)。
- 	est.ps1 三条新断言（SprayRinseVolumeLimitValue、spray_rinse_volume_limit_ml、c+limit 日志）。**96 项全过**。
- 产物 uild\0.9.14\ProbablyHardcoreLiquid.dll 817,152 B，SHA256 C47BA5963B9235F7…，已部署（manifest 0.9.14）。
## v0.9.15（2026-11-01）余量默认 1ml，0 表示"只允许在残留上限内"

- spray_rinse_volume_limit_ml 默认 **5 → 1**；访问器兜底同步改 1。
- 判定改为无条件比较：cap = c + max(0, limitMl)×1000，limitMl = 0 时 cap = c —— 即"超过残留上限就不洗"，
  不再是"0 = 不限制"。
- 说明文案同步改写（中文/英文 tooltip）。
- 	est.ps1 96 项仍全过；产物 uild\0.9.15\ProbablyHardcoreLiquid.dll 817,152 B，
  SHA256 6D38121482844075…，已部署（manifest 0.9.15）。