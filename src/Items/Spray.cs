using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyHardcoreLiquid
{
    // ===== 喷壶（Spray Bottle）v0.7.1 =====
    // 用户需求：2×2 格子、单一 1000ml、液体只能倒掉（不能整壶转移给其他容器）、
    // 拖到别的液体容器 = 少量多次清洗（连续稀释置换模型，默认 M/c = 3）、
    // 化学家开局送满水（水质可选）、化学商人/博士概率卖空壶、80 信用点。
    // 贴图按液位切换：没水=spray_empty、<1/3=spray_quarter、<2/3=spray_half、否则 spray_full。
    public static class Spray
    {
        public const string SprayId = "dsc_spray_bottle";
        public const int CapacityMl = 1000;
        public const int SquirtUl = 20000; // 每股 20ml

        // 清洗流程中置位，让残留结算 patch 跳过（量由清洗自己管理）
        public static bool Rinsing;

        private static bool _giftGiven;
        private static bool _pendingGift;
        // v0.8.3：物品只创建一次，落在 _pendingItem 里等库存容器就绪再放进去。
        private static GameItem _pendingItem;
        private static int _giftAttempts;
        private static bool _spritesReady;
        private static bool _gridWarned;
        private static ModItemDirectory _dir;
        private static System.Random _rng = new System.Random();

        public static bool IsSpray(GameItem item)
        {
            return item != null && item.identifier == SprayId;
        }

        // 获取 mod 物品目录（注册/创建喷壶的唯一正确通道）。
        // ItemDirectory.CreateEmptyItem 是原版目录的静态工厂，不会检索 mod 目录——
        // 之前喷壶在游戏里完全不出现就是栽在这里。
        private static ModItemDirectory GetDir()
        {
            try
            {
                if (_dir == null)
                {
                    _dir = (ModItemDirectory)UnityEngine.Object.FindObjectOfType(typeof(ModItemDirectory));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray dir lookup failed: " + e.Message);
            }
            return _dir;
        }

        // ---- 贴图：4 张 PNG 作为 DLL 内嵌资源，运行时灌进自己 LoadedMod 的 Sprites 字典 ----
        // v0.8.2：画布不再是固定 74×74，而是运行时按 UISettings.current.gridSize 生成 2g×2g。
        // 原因（IL 实证）：GameItem.SetSpriteAndShapeFromMod 里
        //   cols = (int)rect.width / gridSize;  rows = (int)rect.height / gridSize;
        // 然后只采每格「中心像素」的 alpha 决定该格是否占位。74px 在 g≈24 时算出 3×3，
        // 中心透明的格子被丢掉 → 形状变成用户看到的 7 格怪形状，仓库也就放不下。
        // 现在：画布恰为 2g×2g（行列数必为 2），并把四个格中心锚点强制为不透明，
        // 保证形状恒为满 2×2。
        private const float SprayPixelsPerUnit = 100f;
        private static int _canvasSize;
        private static int _canvasHeight;

        // gridSize 是 UISettings 上的 public int 字段（Awake 只做 current = this，
        // 数值来自场景序列化），直接读即可，不需要反射 → 不会给模组扫描器留下 reflection 痕迹。
        private static int CurrentGridSize()
        {
            try
            {
                UISettings ui = UISettings.current;
                if (ui != null && ui.gridSize > 0) return ui.gridSize;
            }
            catch (Exception) { }
            return 0;
        }

        private static void EnsureSprites()
        {
            if (_spritesReady) return;
            // gridSize 在标题画面/场景切换时可能还读不到：那就先不缓存，等下次（进游戏后）重试，
            // 否则会把 74px 兜底画布永久缓存下来，占格又变回 3×3。
            if (CurrentGridSize() <= 0)
            {
                // 只有在完全没有 UI 的场景才走到这里；记录一次即可，避免刷屏
                if (!_gridWarned)
                {
                    _gridWarned = true;
                    Debug.LogWarning("[HardcoreLiquid] gridSize unavailable yet; spray sprites deferred");
                }
                return;
            }
            _spritesReady = true;
            try
            {
                LoadedMod own = null;
                List<LoadedMod> mods = ModLoader.Mods;
                if (mods != null)
                {
                    for (int i = 0; i < mods.Count; i++)
                    {
                        LoadedMod lm = mods[i];
                        if (lm != null && lm.Manifest != null
                            && lm.Manifest.ID == ProbablyHardcoreLiquidMod.ModId)
                        {
                            own = lm;
                            break;
                        }
                    }
                }
                if (own == null)
                {
                    Debug.LogWarning("[HardcoreLiquid] own LoadedMod not found; spray sprites skipped");
                    return;
                }
                string[] keys = new string[] { "spray_empty", "spray_quarter", "spray_half", "spray_full" };
                int g = CurrentGridSize();
                // v0.8.6：占格 1×2（宽 1 格、高 2 格），画布 = gridSize × 2·gridSize
                int cw = g;
                int ch = g * 2;
                _canvasSize = cw;
                _canvasHeight = ch;
                int loaded = 0;
                int anchorFixes = 0;
                for (int i = 0; i < keys.Length; i++)
                {
                    Texture2D master = LoadEmbeddedTexture(keys[i]);
                    if (master == null) continue;
                    Texture2D tex = master;
                    if (master.width != cw || master.height != ch)
                    {
                        int fixes;
                        tex = BuildCanvas(master, cw, ch, out fixes);
                        anchorFixes += fixes;
                    }
                    Sprite sp = MakeSprite(tex, keys[i]);
                    if (sp != null)
                    {
                        own.Sprites[keys[i]] = sp;
                        loaded++;
                    }
                }
                Debug.Log("[HardcoreLiquid] spray sprites loaded: " + loaded + "/4 (gridSize=" + g
                          + ", canvas=" + cw + "x" + ch + ", anchorFixes=" + anchorFixes + ")");
                // v0.8.3 诊断：把原版 1×1 物品的贴图真实像素尺寸打出来，作为「几格=几像素」的标尺。
                // 1ml 滴管用的是原版图集 Items/water_container3 里的 "1ml" 精灵，占 1×1。
                try
                {
                    Sprite reference = RenderHandler.LoadFromAtlas("Items/water_container3", "1ml");
                    if (reference != null)
                    {
                        Debug.Log("[HardcoreLiquid] ref sprite: vanilla 1x1 dropper = "
                            + (int)reference.rect.width + "x" + (int)reference.rect.height
                            + " px, gridSize=" + g + " → 2x2 should be "
                            + (int)reference.rect.width * 2 + "x" + (int)reference.rect.height * 2 + " px");
                    }
                }
                catch (Exception) { }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray sprite load failed: " + e.Message);
            }
        }

        private static Sprite MakeSprite(Texture2D tex, string key)
        {
            if (tex == null) return null;
            tex.name = key;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;
            Sprite sp = Sprite.Create(tex, new Rect(0f, 0f, (float)tex.width, (float)tex.height),
                new Vector2(0.5f, 0.5f), SprayPixelsPerUnit);
            if (sp == null) return null;
            sp.name = key;
            return sp;
        }

        // v0.8.6：占格从 2×2 改成「宽 1 格 × 高 2 格」。
        // 瓶身本来就是竖长的（母图内容 250×579 ≈ 1:2.3），1×2 画布（1:2）比 2×2 更贴合，
        // 同样的像素预算下瓶子更大更清楚，而且只占一半的仓库格子。
        // 流程：① 裁掉母图四周空白（母图是正方形，瓶身只占中间一条）→
        //       ② 面积平均（box filter）等比缩放到 cw×ch，宽度只差一点时最多横向补到 1.25 倍 →
        //       ③ 两格中心若仍透明，补一个 alpha=1 的锚点（不可见，但形状判定算它占用）。
        private static Texture2D BuildCanvas(Texture2D master, int cw, int ch, out int anchorFixes)
        {
            anchorFixes = 0;
            int mw = master.width;
            int mh = master.height;
            Color32[] src = master.GetPixels32();
            Color32[] dst = new Color32[cw * ch];
            Color32 clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < dst.Length; i++) dst[i] = clear;

            // ① 不透明内容包围盒
            int minX = mw;
            int minY = mh;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < mh; y++)
            {
                int rowBase = y * mw;
                for (int x = 0; x < mw; x++)
                {
                    if (src[rowBase + x].a == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < minX)
            {
                minX = 0;
                minY = 0;
                maxX = mw - 1;
                maxY = mh - 1;
            }
            int bw = maxX - minX + 1;
            int bh = maxY - minY + 1;

            // ② 等比缩放（宽度差 ≤45% 时横向补满：高清母版是 300x790≈0.38，比 16x32=0.5 窄，补满更清楚）
            float scale = Math.Min((float)cw / (float)bw, (float)ch / (float)bh);
            int dw = (int)(bw * scale);
            int dh = (int)(bh * scale);
            if (dw < 1) dw = 1;
            if (dh < 1) dh = 1;
            if (dw < cw && (float)cw / (float)dw <= 1.45f) dw = cw;
            int ox = (cw - dw) / 2;
            int oy = (ch - dh) / 2;

            for (int y = 0; y < dh; y++)
            {
                int sy0 = minY + (int)((long)y * bh / dh);
                int sy1 = minY + (int)(((long)(y + 1) * bh + dh - 1) / dh);
                if (sy1 <= sy0) sy1 = sy0 + 1;
                if (sy1 > maxY + 1) sy1 = maxY + 1;
                for (int x = 0; x < dw; x++)
                {
                    int sx0 = minX + (int)((long)x * bw / dw);
                    int sx1 = minX + (int)(((long)(x + 1) * bw + dw - 1) / dw);
                    if (sx1 <= sx0) sx1 = sx0 + 1;
                    if (sx1 > maxX + 1) sx1 = maxX + 1;

                    // 面积平均：目标像素覆盖的源像素，颜色按 alpha 加权，alpha 取算术平均
                    float sumR = 0f;
                    float sumG = 0f;
                    float sumB = 0f;
                    float sumA = 0f;
                    int n = 0;
                    for (int sy = sy0; sy < sy1; sy++)
                    {
                        int rowBase = sy * mw;
                        for (int sx = sx0; sx < sx1; sx++)
                        {
                            Color32 c = src[rowBase + sx];
                            float wgt = c.a / 255f;
                            sumR += c.r * wgt;
                            sumG += c.g * wgt;
                            sumB += c.b * wgt;
                            sumA += c.a;
                            n++;
                        }
                    }
                    if (n <= 0) n = 1;
                    float aw = sumA / 255f;
                    if (aw < 0.2f) continue;          // 几乎全透明，保持空像素
                    int outA = (int)(sumA / n);
                    if (outA < 1) outA = 1;
                    dst[(oy + y) * cw + (ox + x)] = new Color32(
                        (byte)Clamp255(sumR / aw),
                        (byte)Clamp255(sumG / aw),
                        (byte)Clamp255(sumB / aw),
                        (byte)outA);
                }
            }

            // ③ 1×2 只有一列两行，格高 = cw（gridSize）
            int half = cw / 2;
            for (int row = 0; row < 2; row++)
            {
                int cy = row * cw + cw / 2;
                if (ForceOpaque(dst, cw, ch, half, cy, 0, row * cw, cw)) anchorFixes++;
            }

            Texture2D tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            tex.SetPixels32(dst);
            tex.Apply(false, false);
            return tex;
        }

        private static int Clamp255(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 255f) return 255;
            return (int)(v + 0.5f);
        }

        // 「占格锚点」：格中心若透明，就从本格内最近的可见像素借色，但 alpha 只给 1/255。
        // 形状判定只看 alpha > 0，所以 1 就足够让该格被算作占用；
        // 而 1/255 在屏幕上等于不可见，不会出现"瓶子旁边飘着四个小点"。
        private static bool ForceOpaque(Color32[] px, int w, int h, int cx, int cy, int x0, int y0, int cell)
        {
            if (px[cy * w + cx].a != 0) return false;
            Color32 fallback = new Color32(221, 232, 236, 1);   // 瓶身浅灰蓝 #DDE8EC，alpha 1
            int bestD = int.MaxValue;
            Color32 best = fallback;
            for (int y = y0; y < y0 + cell; y++)
            {
                for (int x = x0; x < x0 + cell; x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    Color32 c = px[y * w + x];
                    if (c.a == 0) continue;
                    int d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = c;
                        best.a = 1;
                    }
                }
            }
            px[cy * w + cx] = best;
            return true;
        }

        private static Texture2D LoadEmbeddedTexture(string key)
        {
            System.IO.Stream s = typeof(Spray).Assembly.GetManifestResourceStream(
                "ProbablyHardcoreLiquid." + key + ".png");
            if (s == null) return null;
            byte[] data = new byte[s.Length];
            int off = 0;
            int n;
            while (off < data.Length && (n = s.Read(data, off, data.Length - off)) > 0)
            {
                off += n;
            }
            s.Dispose();
            Texture2D srcTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(srcTex, data))
            {
                Debug.LogWarning("[HardcoreLiquid] LoadImage failed for " + key);
                return null;
            }
            return srcTex;
        }

        // 按当前液位换贴图：<2%=空、<35%=1/4、<70%=半、否则满。
        // 走官方 mod 贴图通道 SetSpriteAndShapeFromMod（内部会按贴图重算 2×2 占格形状）。
        // 注意：GridShape 是 ref-struct，旧版 csc 无法直接引用，只能走这个字符串 API。
        public static void RefreshSprite(GameItem item)
        {
            if (item == null || item.identifier != SprayId) return;
            try
            {
                EnsureSprites();
                long vol = WaterHelper.GetTotalVolume(item);
                long cap = (long)WaterHelper.GetTotalCapacityML(item) * 1000L;
                if (cap <= 0) cap = CapacityMl * 1000L;
                string key;
                if (vol <= cap * 2 / 100) key = "spray_empty";
                else if (vol < cap * 35 / 100) key = "spray_quarter";
                else if (vol < cap * 70 / 100) key = "spray_half";
                else key = "spray_full";
                item.SetSpriteAndShapeFromMod(ProbablyHardcoreLiquidMod.ModId, key);
                // 注：GridShape 是 ref struct，C#5 编译器无法引用 item.shape / modifiedShape（CS0570），
                // 所以没法在这里直接补 modifiedShape；改由落位流程两段式完成（见 TryPlacePending）。
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray sprite refresh failed: " + e.Message);
            }
        }

        // 工厂主体：完全按移液枪 mod（可调移液枪）的注册模板改写——
        // set_identifier/set_identifierName/SetGameItemType("TOOL")/SetValue/交互回调/onLoaded 修复，
        // 这些之前都缺，是喷壶在游戏里不出现的直接原因。
        private static GameItem CreateSprayCore()
        {
            GameItem item = ItemDirectory.CreateEmptyItem(SprayId);
            if (item == null) return null;
            item.SetName(Loc.T("实验室喷壶(1000ml)", "Lab Spray Bottle (1000ml)"));
            item.identifier = SprayId;
            item.identifierName = SprayId;
            int price = ProbablyHardcoreLiquidMod.Settings.SprayPriceValue;
            if (price <= 0) price = 80;
            item.SetValue((long)price);
            item.unitBaseValue = (long)price;
            item.SetGameItemType("TOOL");
            item.EnableTag("IMPORTANT_TAG", false);
            // capacityMl, basePrice, isFilterContainer, isGraduated, isDropper, isTransparentContainer, sheetPage
            // isTransparent=false：避免原版"按容量换瓶身贴图"的修正器覆盖我们的液位贴图
            WaterHelper.InitLiquidContainerItem(item, CapacityMl, price, false, false, false, false, 0);
            // v0.9.0：原版在 InitLiquidContainerItem 里挂了 onThisTargetItemFunc（b__9），负责
            // "拖到水龙头就装水 / 拖到排水口就倒掉"。我们下面要替换它，所以先把原委托留一份，
            // 否则拖到水龙头会没有任何反应（用户 m01554 反馈的就是这个）。
            try
            {
                Action<GameItem, GameItem> vanilla = item.onThisTargetItemFunc as Action<GameItem, GameItem>;
                if (vanilla != null) VanillaTargets.AddOrUpdate(item, vanilla);
            }
            catch (Exception e) { Debug.LogWarning("[HardcoreLiquid] capture vanilla target failed: " + e.Message); }
            // 交互回调（替换默认整壶倾倒）：目标=排水口→倒掉；目标=水龙头→装水（交给原版）；目标=液体容器→清洗
            item.mayThisTargetItemFunc = Spray.CanSprayTarget;
            item.canThisTargetItemFunc = Spray.CanSprayTarget;
            item.onThisTargetItemFunc = Spray.SprayTargetAction;
            item.maySelectSlotItemFunc = Spray.IsOwnedSlot;
            item.canSelectSlotItemFunc = Spray.IsOwnedSlot;
            item.onLoaded += Spray.RepairSpray;
            try { AudioHelper.InitPlasticBottle(item); }
            catch (Exception e) { Debug.LogWarning("[HardcoreLiquid] spray audio init failed: " + e.Message); }
            RefreshSprite(item);
            return item;
        }

        // 能被拖起当"主动物"：必须是玩家拥有的物品
        public static bool IsOwnedSlot(GameItem item, GameInventory inv, SlotMarker slot)
        {
            try { return GeneralHelper.IsItemOwned(item); }
            catch (Exception) { return true; }
        }

        // 能作为"目标"：排水口/水槽（倒掉）或液体容器（清洗）
        public static bool CanSprayTarget(GameItem source, GameItem target)
        {
            try
            {
                if (source == null || target == null) return false;
                if (source.identifier != SprayId) return false;
                if (source == target) return false;
                if (!GeneralHelper.IsItemOwned(source)) return false;
                if (WaterHelper.IsDrain(target) || WaterHelper.IsFaucet(target)) return true;
                if (target.state != null && target.state.IsTag(LiquidContainerHelper.LIQUID_CONTAINER_TAG))
                {
                    if (!WaterHelper.CanUseWaterContainer(target)) return false;
                    // v0.9.4：空、液体太多、或成分已经和清洗液一样 → 不给操作
                    int tv;
                    return RinseRefusal(source, target, out tv) == null;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 容器当前液体总量（µl）；异常一律当 0 处理，避免误判成"有东西"
        private static int TargetVolume(GameItem item)
        {
            try { return WaterHelper.GetTotalVolume(item); }
            catch (Exception) { return 0; }
        }

        private static int SprayVolume(GameItem spray)
        {
            try { return WaterHelper.GetTotalVolume(spray); }
            catch (Exception) { return 0; }
        }

        // v0.9.4 清洗前判定：返回 null 才允许清洗，否则返回原因（用于日志）
        //   ① 容器里没有任何液体        → 没有可洗的东西
        //   ② 液体量 > 该容器残留上限 c + 余量（默认 5ml）→ 那是内容物，别当残渣洗掉
        //   ③ 喷壶只剩自己的残渣        → 没水可喷
        //   v0.9.13 曾把 ② 整个删掉（导致几乎所有容器都点不动）；v0.9.14 改回"可配置的余量"。
        private static string RinseRefusal(GameItem spray, GameItem target, out int targetVol)
        {
            targetVol = TargetVolume(target);
            if (targetVol <= 0) return "empty";
            long c = TargetResidueUl(target);
            float limitMl = ProbablyHardcoreLiquidMod.Settings.SprayRinseVolumeLimitValue;
            if (limitMl < 0f) limitMl = 0f;   // 负数按 0 处理
            long cap = c + (long)(limitMl * 1000f);   // 余量 0 → 阈值就是残留上限 c
            if (targetVol > cap)
                return "liquid " + targetVol + "ul > c+limit " + cap + "ul (c=" + c + "ul + " + limitMl.ToString("0.##") + "ml)";
            if (SprayAvailableUl(spray) <= 0) return "spray holds only its own residue (" + SprayVolume(spray) + "ul <= " + SprayResidueUl(spray) + "ul)";
            return null;
        }

        // 每把喷壶的原版 onThisTargetItemFunc（负责水龙头装水/排水口倒掉）
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameItem, Action<GameItem, GameItem>> VanillaTargets
            = new System.Runtime.CompilerServices.ConditionalWeakTable<GameItem, Action<GameItem, GameItem>>();

        // 目标动作（替代原版整壶倾倒）
        public static void SprayTargetAction(GameItem source, GameItem target)
        {
            try
            {
                if (source == null || target == null || source.identifier != SprayId) return;
                // 水龙头：装水交给原版（保留当日水质/净水器/升级判定），我们自己不写纯度逻辑
                if (WaterHelper.IsFaucet(target))
                {
                    Action<GameItem, GameItem> vanilla;
                    if (VanillaTargets.TryGetValue(source, out vanilla) && vanilla != null)
                    {
                        vanilla(source, target);
                    }
                    RefreshSprite(source);
                    return;
                }
                if (WaterHelper.IsDrain(target))
                {
                    WaterHelper.EmptyContainer(source); // 倒下水道
                }
                else if (target.state != null
                    && target.state.IsTag(LiquidContainerHelper.LIQUID_CONTAINER_TAG))
                {
                    // v0.9.4：空 / 液体超过残留上限 / 成分已与清洗液一致 → 不消耗喷壶里的水
                    int tv;
                    string why = RinseRefusal(source, target, out tv);
                    if (why != null)
                    {
                        Debug.Log("[HardcoreLiquid] spray: skip rinse (" + why + ")");
                        return;
                    }
                    Rinse(source, target); // 少量多次清洗
                }
                RefreshSprite(source);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray target action failed: " + e.Message);
            }
        }

        // 读档修复（仿移液枪 RepairLoadedItem）：恢复名称/价值/形状/贴图
        public static void RepairSpray(GameItem item)
        {
            try
            {
                if (item == null || item.identifier != SprayId) return;
                item.SetName(Loc.T("实验室喷壶(1000ml)", "Lab Spray Bottle (1000ml)"));
                int price = ProbablyHardcoreLiquidMod.Settings.SprayPriceValue;
                if (price <= 0) price = 80;
                item.SetValue((long)price);
                item.unitBaseValue = (long)price;
                item.SyncModifiedState();
                RefreshSprite(item);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray load repair failed: " + e.Message);
            }
        }

        // 博士货架：官方 ModHook 通道（移液枪 mod 同款）——Inventor 摆库存时按概率加一把空喷壶。
        // v0.9.13：这个 hook 会随"摆放物品"被反复调用（货架放 6 件就调 6 次），
        // 逐个物品掷骰会把 0.25 放大成 1-(1-0.25)^6 ≈ 82%。改为：同一次补货只掷一次、
        // 货架上已有喷壶就不再加，这样配置值才是"每天出现概率"。
        private static object _doctorStockList;
        private static float _doctorStockTime = -999f;

        public static void AddToDoctorStock(List<GameItem> list)
        {
            try
            {
                if (list == null) return;
                if (!ProbablyHardcoreLiquidMod.Settings.SprayEnabledValue) return;
                float prob = ProbablyHardcoreLiquidMod.Settings.SprayDoctorProbValue;
                if (prob <= 0f) return;

                // 同一次补货（同一个 list 实例，或 3 秒内的重复回调）只掷一次
                if (object.ReferenceEquals(list, _doctorStockList)) return;
                float now = Time.time;
                if (now - _doctorStockTime < 3f) return;
                _doctorStockList = list;
                _doctorStockTime = now;

                // 货架上已经有一把喷壶就不再补
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && list[i].identifier == SprayId) return;
                }

                if (Spray.Roll(prob))
                {
                    GameItem spray = CreateSpray();
                    if (spray != null)
                    {
                        list.Add(spray);
                        Debug.Log("[HardcoreLiquid] doctor got a spray bottle in stock (p=" + prob.ToString("0.00") + ")");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] doctor stock hook failed: " + e.Message);
            }
        }

        public static GameItem CreateSpray()
        {
            try
            {
                ModItemDirectory d = GetDir();
                GameItem item;
                if (d != null)
                {
                    if (!d.Has(SprayId))
                    {
                        d.Add(SprayId, delegate() { return CreateSprayCore(); });
                    }
                    item = d.Create(SprayId);
                }
                else
                {
                    item = ItemDirectory.CreateEmptyItem(SprayId);
                }
                if (item == null)
                {
                    Debug.LogWarning("[HardcoreLiquid] spray create failed (dir=" + (d != null ? "ok" : "null") + ")");
                }
                return item;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray create failed: " + e.Message);
                return null;
            }
        }

        public static void Register(ModItemDirectory dir)
        {
            if (dir == null) return;
            _dir = dir;
            try
            {
                if (!dir.Has(SprayId))
                {
                    dir.Add(SprayId, delegate() { return CreateSprayCore(); });
                    Debug.Log("[HardcoreLiquid] spray bottle registered (1000ml)");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray register failed: " + e.Message);
            }
        }

        // 目标容器残留体积 c（ml）：与残留规则同款公式——min(容量×比例, 上限)
        private static long TargetResidueUl(GameItem target)
        {
            long capMl = ContainerCapacityMl(target);
            return ResidueUlForParts(capMl, ToDouble(WaterHelper.GetLiquidParts(target)));
        }

        // 喷壶自身的残余体积（µl）：和容器同一套公式，喷壶倒不出去的就是这么多
        private static long SprayResidueUl(GameItem spray)
        {
            long capMl = ContainerCapacityMl(spray);
            if (capMl <= 0) capMl = 1000;
            return ResidueUlForParts(capMl, ToDouble(WaterHelper.GetLiquidParts(spray)));
        }

        private static long ContainerCapacityMl(GameItem item)
        {
            long capMl = 0;
            try { capMl = WaterHelper.GetTotalCapacityML(item); } catch (Exception) { }
            if (capMl <= 0)
            {
                try { capMl = WaterHelper.GetTotalCapacity(item) / 1000; } catch (Exception) { }
                if (capMl <= 0) capMl = 500;
            }
            return capMl;
        }

        private static double[] ToDouble(int[] parts)
        {
            if (parts == null) return new double[0];
            double[] d = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++) d[i] = parts[i];
            return d;
        }

        // v0.9.9：残留体积按"给定组成"计算——清洗过程中组成一直在变，粘度因子跟着变，
        // 所以 c 不是一个常数，而是组成的函数 c(组成)。
        //   c(组成) = min( 容量ml × 比例% × 粘度因子(组成) , 上限ml ) × 1000   [µl]
        private static long ResidueUlForParts(long capMl, double[] parts)
        {
            double pct = ProbablyHardcoreLiquidMod.Settings.PercentValue;
            long maxMl = ProbablyHardcoreLiquidMod.Settings.MaxMlValue;
            double cMl = (double)capMl * pct / 100.0;
            cMl *= ResidueLogic.ViscosityFactor(parts);
            if (maxMl > 0 && cMl > maxMl) cMl = maxMl;
            long cul = (long)(cMl * 1000.0);
            if (cul <= 0) cul = 1000; // 至少 1ml，防除零
            return cul;
        }

        // 喷壶实际能喷出去的量 = 现有液体 − 它自己倒不出去的残余
        private static long SprayAvailableUl(GameItem spray)
        {
            long avail = (long)SprayVolume(spray) - SprayResidueUl(spray);
            return avail > 0 ? avail : 0;
        }

        private static int TargetFreeUl(GameItem target)
        {
            int cap = WaterHelper.GetTotalCapacity(target);
            if (cap <= 0) return int.MaxValue;
            return cap - WaterHelper.GetTotalVolume(target);
        }

        private static void TransferSquirt(GameItem spray, GameItem target, int volUl)
        {
            int[] parts = WaterHelper.GetLiquidParts(spray);
            if (parts == null || parts.Length == 0) return;
            int[] dist = WaterHelper.SplitProportionally(parts, volUl);
            List<Liquid> liquids = Liquid.Liquids;
            for (int i = 0; i < dist.Length && i < liquids.Count; i++)
            {
                if (dist[i] > 0)
                {
                    WaterHelper.AddLiquid(target, liquids[i].identifier, dist[i]);
                }
            }
            WaterHelper.Remove(spray, volUl);
        }

        // 清洗：连续稀释 / CSTR 置换模型的闭式解（v0.8.0 改写）。
        // 目标容器的固有残留为 c；把总量 M = rinse_ratio × c 的清洗液分 n 次喷入，
        // 每次混匀后倒空到只剩 c，n 次后残留比例 = (c/(c+M/n))^n → e^(-M/c)。
        // 这里直接落 n→∞ 的闭式解：清洗后目标体积 = c，组成 = p·A + (1−p)·B，p = e^(-M/c)。
        // 目标里多出来的脏液在清洗中一并倒掉——那正是用户手动"倒空到 c"的那一步。
        public static void Rinse(GameItem spray, GameItem target)
        {
            if (Rinsing) return;
            Rinsing = true;
            try
            {
                float ratioCfg = ProbablyHardcoreLiquidMod.Settings.SprayRinseRatioValue;
                if (ratioCfg <= 0f)
                {
                    Debug.Log("[HardcoreLiquid] spray rinse disabled (rinse_ratio <= 0), pour rejected");
                    return;
                }
                long cUl = TargetResidueUl(target);
                int sprayVol = WaterHelper.GetTotalVolume(spray);
                if (sprayVol <= 0) return;
                int[] sprayParts = WaterHelper.GetLiquidParts(spray);
                int[] targetParts = WaterHelper.GetLiquidParts(target);
                if (sprayParts == null || sprayParts.Length == 0) return;
                int targetVol = WaterHelper.GetTotalVolume(target);

                // 实际清洗倍率：n 是配置的期望值，但喷壶能倒出去的只有"现有液体 − 自身残余"
                // M_avail < n·c 时，按实际差值算倍率（k 可以是小数）
                long availUl = SprayAvailableUl(spray);
                if (availUl <= 0)
                {
                    Debug.Log("[HardcoreLiquid] spray rinse skipped: spray holds only its own residue");
                    return;
                }
                double k = ratioCfg;
                double maxK = (double)availUl / (double)cUl;
                if (k > maxK)
                {
                    k = maxK;
                    Debug.Log("[HardcoreLiquid] spray rinse limited by bottle: avail=" + availUl
                        + "ul / c=" + cUl + "ul -> k=" + k.ToString("0.000") + " (want " + ratioCfg.ToString("0.00") + ")");
                }
                if (k <= 0.0) return;

                // 归一化组成：a = 容器原液，b = 清洗液（喷壶里的东西）
                long capMl = ContainerCapacityMl(target);
                List<Liquid> liquids = Liquid.Liquids;
                int n = liquids.Count;
                double[] a = new double[n];
                double[] b = new double[n];
                for (int i = 0; i < n; i++)
                {
                    a[i] = (targetVol > 0 && i < targetParts.Length) ? (double)targetParts[i] / (double)targetVol : 0.0;
                    b[i] = (i < sprayParts.Length) ? (double)sprayParts[i] / (double)sprayVol : 0.0;
                }

                // 实际喷出的总量 M（µl）：倍率 k 按"开始时的残留 c0"折算
                long c0 = ResidueUlForParts(capMl, a);
                long used = (long)Math.Round(k * (double)c0);
                if (used > availUl) used = availUl;
                if (used <= 0) return;

                // v0.9.11：回到最简连续稀释模型——清洗过程中 c 当常数，一步出解。
                //   p = e^(-M/c)，而 M = k·c，所以 p = e^(-k)。
                // 粘度与残留体积只在"算 c 那一步"生效（对数混合粘度 → c = min(容量×比例×V, 上限)）。
                double p = Math.Exp(-k);
                double q = 1.0 - p;

                double[] mix = new double[n];
                double acc = 0.0;
                for (int i = 0; i < n; i++)
                {
                    mix[i] = p * a[i] + q * b[i];
                    acc += mix[i];
                }
                if (acc <= 0.0) return;

                // v0.9.12：清洗过程用常数 c（闭式解），但洗完的残留体积按**新组成**重算——
                // 洗成更粘的东西，剩下的那点就更难倒干净。
                long c1 = ResidueUlForParts(capMl, mix);
                double[] finalUl = new double[n];
                for (int i = 0; i < n; i++) finalUl[i] = mix[i] / acc * (double)c1;

                // 1) 倒空目标（= 用户倒掉超过残留的液体）
                if (targetVol > 0) WaterHelper.Remove(target, targetVol);

                // 2) 回填 c1 体积的混合液，四舍五入后尾差补给最大成分
                long[] addUl = new long[n];
                long sum = 0;
                int dominant = 0;
                for (int i = 0; i < n; i++)
                {
                    addUl[i] = (long)Math.Round(finalUl[i]);
                    if (addUl[i] < 0) addUl[i] = 0;
                    sum += addUl[i];
                    if (finalUl[i] > finalUl[dominant]) dominant = i;
                }
                long diff = c1 - sum;
                if (diff != 0)
                {
                    addUl[dominant] += diff;
                    if (addUl[dominant] < 0) addUl[dominant] = 0;
                }
                for (int i = 0; i < n; i++)
                {
                    if (addUl[i] > 0) WaterHelper.AddLiquid(target, liquids[i].identifier, (int)addUl[i]);
                }

                // 3) 喷壶扣掉 M
                if (used > 0) WaterHelper.Remove(spray, (int)used);

                Debug.Log("[HardcoreLiquid] spray rinse: target=" + target.identifier
                    + " c0=" + c0 + "ul c1=" + c1 + "ul dumped=" + targetVol + "ul sprayed=" + used + "ul (M/c0=" + k.ToString("0.###")
                    + ", want " + ratioCfg.ToString("0.00") + ") retained=" + (p * 100.0).ToString("0.##") + "%");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray rinse failed: " + e.Message);
            }
            finally
            {
                Rinsing = false;
                RefreshSprite(spray);
            }
        }

        // 开局礼装液：pure/high_quality/base
        public static void FillForGift(GameItem item, string fill)
        {
            int cap = WaterHelper.GetTotalCapacity(item);
            if (cap <= 0) cap = CapacityMl * 1000;
            if (fill == "high_quality")
            {
                WaterHelper.AddHighQualityWater(item, cap);
            }
            else if (fill == "base")
            {
                WaterHelper.AddBaseWater(item, cap);
            }
            else
            {
                WaterHelper.AddPureWater(item, cap);
            }
        }

        public static void RequestGiftIfChemist()
        {
            try
            {
                if (!ProbablyHardcoreLiquidMod.Settings.SprayEnabledValue) return;
                if (!ProbablyHardcoreLiquidMod.Settings.SprayStartGiftValue) return;
                if (!PlayerStore.IsInstanceExist()) return;
                PlayerStore ps = PlayerStore.Instance;
                if (ps == null || !ps.IsChemistStart()) return;
                _pendingGift = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray gift request failed: " + e.Message);
            }
        }

        public static void GiftIfChemist()
        {
            if (_giftGiven) return;
            try
            {
                if (!ProbablyHardcoreLiquidMod.Settings.SprayStartGiftValue) return;
                if (!PlayerStore.IsInstanceExist()) return;
                PlayerStore ps = PlayerStore.Instance;
                if (ps == null || !ps.IsChemistStart()) return;
                // 加载化学家存档时也送（不要求新开局）：HandleInitialItem 只在开新档时跑，
                // 用户测试习惯是读旧档，所以只要 IsChemistStart 且本会话未送过就发。
                _pendingGift = false;
                // v0.8.3：物品只造一次，放不进库存就留着等下一个晚阶段钩子，绝不丢弃。
                if (_pendingItem == null)
                {
                    GameItem created = CreateSpray();
                    if (created == null) return;
                    FillForGift(created, ProbablyHardcoreLiquidMod.Settings.SprayGiftFillValue);
                    RefreshSprite(created);
                    _pendingItem = created;
                    int size = 0;
                    try { size = created.GetItemSize(); } catch (Exception) { }
                    Debug.Log("[HardcoreLiquid] spray gift diag: size=" + size
                        + " spritePath=" + (created.spritePath == null ? "null" : created.spritePath));
                }
                TryPlacePending();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray gift failed: " + e.Message);
            }
        }

        // 晚阶段钩子（OnGameLoadedLate / OnShutterOpenedEarly 等）重试落位。
        // 存档里查不到 dsc_spray_bottle（save_44.es3）说明 v0.8.2 的 MoveItemToInventory 是空操作：
        // 它内部就是 GraphUtils.TryAcceptAll(EmporiumEntry.Instance.invElement, item, -1) 并丢弃返回值，
        // 在 OnGameLoadedInit 那个时刻 EmporiumEntry.Instance / invElement 还没准备好 → 静默什么都不做。
        public static void TryGiftPending()
        {
            if (_giftGiven || _pendingItem == null) return;
            try
            {
                if (!ProbablyHardcoreLiquidMod.Settings.SprayStartGiftValue) return;
                TryPlacePending();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] spray gift retry failed: " + e.Message);
            }
        }

        // 真正落位：只有 TryAcceptAll 返回 0（申请量 − 实转移 = 0，即全部塞进库存）才算成功。
        // 绝不走 GraphUtils.EmporiumTryAdd——那条路在放不下时会 DestroyIfUnplaced 直接销毁物品。
        // v0.8.7：一次尝试依次试多个容器（背包格子 → 背包物品 → 后台 → 隐藏柜 → 前台），
        // 并把每个容器的 CanAccept / 锁状态 / 物品尺寸全打进日志，一次运行就能定位卡在哪。
        private static void TryPlacePending()
        {
            GameItem item = _pendingItem;
            if (item == null) return;
            _giftAttempts++;
            EmporiumEntry entry = null;
            try { entry = EmporiumEntry.Instance; } catch (Exception) { }
            if (entry == null)
            {
                Debug.Log("[HardcoreLiquid] spray gift deferred #" + _giftAttempts + ": EmporiumEntry.Instance=null");
                return;
            }

            // v0.9.0：只放"店铺仓库"= invElement（SetBigInv 28×10 / SetSmallInv 16~20×10 那块大网格）。
            //   backInvinvElement = 谈判柜台（v0.8.9 放这里，用户 m01554 明确不要）
            //   frontInvinvElement = 卷帘门外展示区（物品进去锁死，禁用）
            // 放不下就保留物品每秒重试，绝不往别的地方丢。
            GraphNodeStorage inv = null;
            try { inv = entry.invElement; } catch (Exception) { }
            if (inv == null)
            {
                Debug.Log("[HardcoreLiquid] spray gift deferred #" + _giftAttempts + ": invElement=null");
                StartPump();
                return;
            }

            // v0.9.2：先打"已归属"标签再落位。店铺仓库 invElement 只收已归属物品，
            //   而 0.8~0.9 一直是落位成功后才 EnableTag → 仓库永远 canAccept=False（日志实测 22 次全拒）。
            try { item.EnableTag("IS_OWNED_TAG", true); } catch (Exception) { }

            // 新造物品的 modifiedShape 可能还没算过；先让游戏自己算一遍，再让库存重算占用格。
            int modSize = -1;
            try { item.ValidateShapeState(); } catch (Exception) { }
            try { modSize = item.GetModifiedItemSize(); } catch (Exception) { }
            GameInventory gi = inv as GameInventory;
            if (gi != null) { try { gi.Validate(); } catch (Exception) { } }

            string desc = Describe(inv, item);
            int remain = TryAccept(inv, item);            if (remain == 0)
            {
                Debug.Log("[HardcoreLiquid] spray place #" + _giftAttempts + " invElement: " + desc
                    + " modSize=" + modSize);
                Accept(item, "invElement");
                return;
            }

            Debug.Log("[HardcoreLiquid] spray gift deferred #" + _giftAttempts + ": invElement(remain=" + remain
                + ":modSize=" + modSize + ":" + desc + ":" + WhyRefused(gi, item) + ")");

            // 兜底：仓库被拒超过 10 次就退到谈判柜台（店内、可拖动），绝不放到卷帘门外。
            if (_giftAttempts > 10)
            {
                GraphNodeStorage back = null;
                try { back = entry.backInvinvElement; } catch (Exception) { }
                if (back != null && TryAccept(back, item) == 0)
                {
                    Debug.LogWarning("[HardcoreLiquid] invElement refused " + _giftAttempts + " times, fell back to backInvinvElement");
                    Accept(item, "backInvinvElement(fallback)");
                    return;
                }
            }
            StartPump();
        }

        // v0.9.2 诊断：把"库存为什么拒收"的关键开关打出来（mayInventoryAddItemFunc 是每个库存自己的准入委托）
        private static string WhyRefused(GameInventory gi, GameItem item)
        {
            if (gi == null) return "type=notGameInventory";
            string s = "";
            try { s += "type=" + gi.GetType().Name; } catch (Exception) { }
            try
            {
                Func<GameItem, GameInventory, bool> f = gi.mayInventoryAddItemFunc;
                s += f == null ? " mayAddItem=<null>" : (" mayAddItem=" + f(item, gi));
            }
            catch (Exception e) { s += " mayAddItem=EX:" + e.GetType().Name; }
            try { s += " overrideLockInsert=" + gi.overrideLockInsert; } catch (Exception) { }
            return s;
        }

        // 统一封装：TryAcceptAll 返回 0 表示全部收下
        private static int TryAccept(GraphNodeStorage target, GameItem item)
        {
            try { return GraphUtils.TryAcceptAll(target, item, -1); }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] TryAcceptAll threw: " + e.Message);
                return -1;
            }
        }

        private static void Accept(GameItem item, string where)
        {
            try { item.EnableTag("IS_OWNED_TAG", true); } catch (Exception) { }
            int size = 0;
            try { size = item.GetItemSize(); } catch (Exception) { }
            _pendingItem = null;
            _giftGiven = true;
            Debug.Log("[HardcoreLiquid] spray gift placed into " + where + ", size=" + size
                + " (attempt " + _giftAttempts + ")");
            StopPump();
        }

        // 新开局时把"已送过"状态清零：静态字段跨新档残留会让第二个新档起就不送壶。
        public static void ResetGiftState()
        {
            StopPump();
            _pendingItem = null;
            _giftGiven = false;
            _pendingGift = false;
            _giftAttempts = 0;
            _pumpStarted = -1f;
        }

        // 每个候选容器打一行：能不能收、锁没锁、物品几格几件、当前有没有父容器
        private static string Describe(GraphNodeStorage inv, GameItem item)
        {
            string s = "";
            try { s += "canAccept=" + GraphUtils.CanAccept(inv, item); } catch (Exception) { s += "canAccept=?"; }
            GameInventory gi = inv as GameInventory;
            if (gi != null)
            {
                try { s += " maySlot=" + gi.MayHaveValidInventorySlot(item); } catch (Exception) { }
                try { s += " insertLocked=" + gi.IsInsertLocked(); } catch (Exception) { }
                try { s += " supportsGroup=" + gi.SupportsGroupPlacement; } catch (Exception) { }
                try { s += " children=" + gi.childItems.Count; } catch (Exception) { }
            }
            try { s += " size=" + item.GetItemSize() + " units=" + item.unitCount; } catch (Exception) { }
            try
            {
                GameInventory par = item.parentInventory;
                s += " parent=" + (par == null ? "null" : par.GetType().Name);
            }
            catch (Exception) { }
            return s;
        }

        // —— 落位失败时的重试泵 ——
        // 背包窗口关着时 invElement 可能是锁定/未激活的，靠定时重试等到用户打开背包。
        private static GiftPump _pump;
        private static float _pumpStarted = -1f;

        private static void StartPump()
        {
            if (_pump != null) return;
            try
            {
                GameObject go = new GameObject("HardcoreLiquidGiftPump");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _pump = go.AddComponent<GiftPump>();
                _pumpStarted = Time.realtimeSinceStartup;
                Debug.Log("[HardcoreLiquid] gift pump started");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] gift pump start failed: " + e.Message);
            }
        }

        public static void StopPump()
        {
            try
            {
                if (_pump != null)
                {
                    UnityEngine.Object.Destroy(_pump.gameObject);
                }
            }
            catch (Exception) { }
            _pump = null;
        }

        public static void PumpTick()
        {
            if (_pendingItem == null || _giftGiven) { StopPump(); return; }
            if (_pumpStarted > 0f && Time.realtimeSinceStartup - _pumpStarted > 600f) { StopPump(); return; }
            TryPlacePending();
        }

        public static void Reset()
        {
            _giftGiven = false;
            _spritesReady = false;
            _pendingItem = null;
            _giftAttempts = 0;
        }

        public static bool Roll(double prob)
        {
            return _rng.NextDouble() < prob;
        }
    }
}
