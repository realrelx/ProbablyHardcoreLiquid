# Probably Hardcore Liquid（可能是困难的液体）

给《深空当铺 (Probably Stolen)》的硬核液体 mod：**液体再也倒不干净**，并给你一把能把它洗干净的喷壶。

Steam 创意工坊：<https://steamcommunity.com/sharedfiles/filedetails/?id=3830066983>
当前版本：**0.9.15** · 游戏版本 0.46D · 零能力声明

---

## 机制

### 1. 固有残留 R

每个液体容器都带一份永远倒不出去的残留：

```
R = min(容器最大容积 × 残留比例% × 粘度因子, 残留上限)
```

- 倾倒、转移、倒排水口、清空酒瓶，都只带走 `总量 − R`，两边组成同比例。
- 只剩 R 时倒不出去；兑入新液、总量超过 R 才能再倒出 `总量 − R`。
- 默认 0.5%、上限 5ml：500ml 瓶 + 纯水 → 2.5ml 残留。

### 2. 粘度（真实物理值，20°C，水 = 1）

覆盖原版全部 37 种液体成分：酒精 1.2、葡萄酒 1.5、浓盐酸 2.0、浓烧碱 13、咖啡渣浆 5、烹饪油 80、糖浆 60、淀粉糊 100、甲醇类 0.7……未登记的（含其他 mod 的液体）按水处理。

- **混合按对数混合**（Arrhenius / Grünberg-Nissan 式）：`ln η_mix = Σ(体积ᵢ·ln ηᵢ) / Σ体积ᵢ`。
- **粘度因子** `= 1 + (η^(1/7) − 1) × viscosity_scale`：粘度 10 倍，残留只多约 1.39 倍。`viscosity_scale = 0` 关闭粘度，全按水算。

### 3. 实验室喷壶

1000ml，占 **1×2 格**，约 80 信用点。

| 操作 | 结果 |
| --- | --- |
| 拖到液体容器 | 连续稀释清洗：喷量 `M = 清洗倍率 × 该容器残留 c`，清洗后残留比例 `p = e^(−M/c)` ≈ **4.98%**（倍率 3） |
| 拖到水龙头 | 装水，沿用当天水质与净水器判定 |
| 拖到排水口 | 倒空 |
| 拖到其他物品 | 无反应（不能整壶转移） |

- 残留 c 只在**清洗开始时**计算一次（`c = 容量 × 比例% × V(η_mix)`，受上限封顶），清洗过程中当常数——所以 `p = e^(−k)` 是一步闭式解。
- 水不够时 k 被钳到「可用水量 / c」，日志会打 `spray rinse limited by bottle`。
- 贴图为 300×790 高清母版，运行时按 `gridSize` 缩到 `1×2` 格；四态（空 / 1/4 / 半 / 满）轮廓一致。
- 化学家开局送一把满纯水（水质可配）；博士与下层区化学商人有概率出售空壶。

### 4. 清洗前判定

| 情形 | 结果 |
| --- | --- |
| 目标是空容器 | 不响应 |
| 容器液体 > 残留上限 + 可配余量（默认 1ml） | 拒绝，日志 `skip rinse` |
| 喷壶只剩自己的残留 | 拒绝 |

---

## 构建

本机无需 .NET SDK，走 .NET Framework 自带的 `csc.exe`（C#5）：

```powershell
$env:PROBABLY_STOLEN_PATH = 'D:\steam\steamapps\common\Probably Stolen'
.\build.ps1            # → build\<版本>\ProbablyHardcoreLiquid.dll + manifest.xml
.\test.ps1             # 96 项离线检查
.\deploy.ps1           # 装进 <游戏>\Mods\ProbablyHardcoreLiquid（游戏须退出）
```

`-GamePath` 可覆盖环境变量；两个脚本都不硬编码游戏目录。

**引用程序集**来自游戏 `Probably Stolen_Data\Managed`：`netstandard.dll`、`mscorlib.dll`、`UnityEngine.CoreModule.dll`、`UnityEngine.ImageConversionModule.dll`、`Assembly-CSharp.dll`、`0Harmony.dll`。

> PowerShell 5.1 读取**无 BOM** 的 UTF-8 `.ps1` 会按 GBK 解析并报语法错误——本仓库的 `.ps1` 一律 UTF-8 **with BOM**。

---

## 目录

```
src/
  Core.cs                       IMod 入口、生命周期、ModHook 订阅
  Infrastructure/Settings.cs    ModConfig 定义（残留、粘度表、喷壶）
  Infrastructure/Loc.cs         中英文案
  Infrastructure/GiftPump.cs    落位重试泵（MonoBehaviour）
  Items/Spray.cs                喷壶：创建 / 落位 / 清洗 / 贴图
  Mechanics/Residue/ResidueLogic.cs   残留结算与粘度
  Mechanics/Residue/Patches.cs        液体出口补丁
  Mechanics/Residue/DropperFix.cs     滴管「只剩残渣仍可吸」
Textures/Items/                 4 张 300×790 高清母版
Textures/Source/                原始 2×2 素材表
scripts/make_spray_textures.ps1 从素材表重新生成母版
docs/                           设计、贴图规格、修复记录、工坊文案
test.ps1                        96 项离线断言（manifest / 贴图 / 红线 / 常量 / 模型）
```

---

## 配置

设置菜单：Mods 列表 → Probably Hardcore Liquid → 设置。数值项是**自由输入框**（可填小数），容器范围是下拉框。配置文件 `ModConfigs\ProbablyHardcoreLiquid.json` 是权威来源。

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `residue_percent` | 0.5 | 残留比例（百分数，1 = 1%），0 = 关闭 |
| `max_residue_ml` | 5 | 残留上限 ml，0 = 不设上限 |
| `container_scope` | all | `all` / `vanilla` |
| `viscosity_scale` | 1 | 粘度缩放，0 = 关闭 |
| `viscosity_*` | 见菜单 | 37 种液体成分粘度表，逐项可调 |
| `spray_enabled` | true | 喷壶总开关 |
| `spray_rinse_ratio` | 3 | 清洗倍率 k（可填 2.5 等小数） |
| `spray_rinse_volume_limit_ml` | 1 | 可洗余量：容器液体 > 残留上限 + 该值则拒绝 |
| `spray_start_gift` | true | 化学家开局送壶 |
| `spray_gift_fill` | pure | 赠送水质：`pure` / `high_quality` / `base` |
| `spray_chemist_weight` | 10 | 化学商人上架权重（%） |
| `spray_doctor_prob` | 0.25 | 博士每日判定概率 |
| `spray_price` | 80 | 售价 |

---

## 已知限制

- 直接饮用（sipping）不残留：那是"喝"，不是"倒"；原版相关方法带 byref 参数，刻意不碰。
- 分装（Split Water）分出的新瓶由原版内部创建、拿不到引用：源瓶残留正常保留；仅当瓶中总量不足 2×R 时可能有几 µl 微小误差。
- 残留不改变原版水质/价格系统：残留成分就是原液体成分，鉴定与卖价走原版逻辑。

---

## 兼容与安全

- **零能力声明**：不申请 process / steam / files / 网络等任何能力。
- 不修改夜间机器结算（净水器、蒸发器、RAS 的每夜耗水不留残留）、不动存档格式。
- 与其他液体 mod 共存：未登记粘度的成分按水处理。
- 字符串 ID 一律 `ProbablyHardcoreLiquid` 前缀；Harmony 补丁只用自己的 ID。

---

## 致谢

- 内容与代码：DeepSeek AI
- 封面与贴图：GPT 生成
- 游戏：《深空当铺 / Probably Stolen》(Questing Goose Studio)

## 许可

MIT License，见 [LICENSE](LICENSE)。
