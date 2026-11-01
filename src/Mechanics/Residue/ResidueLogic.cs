using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyHardcoreLiquid
{
    public sealed class ResidueCapture
    {
        public GameItem Source;
        public GameItem Target; // 转移目标容器；排水口/清空类动作为 null
        public int TotalBefore;
        public int[] PartsBefore;
    }

    public static class ResidueLogic
    {
        // 内部操作标志：结算时对目标容器做 Remove 会再次进入 patch，
        // 用这个标志让嵌套调用不捕获、不结算。
        public static bool InternalOp;

        // ModItemDirectory 是场景里的 MonoBehaviour，惰性查找并缓存
        private static ModItemDirectory _modDir;
        private static bool _modDirWarned;

        // ===== 新残留模型（v0.5，v0.6 加粘度，v0.6.4 η^(1/7)）=====
        // 残留体积 = 容器最大容积 × 残留比例（百分数）× 粘度因子
        // 粘度因子 = 1 + (η^(1/7) − 1) × viscosity_scale
        //   ——药品灌装 HUV 经验模型（V_HU = V_HU,water × η^(1/7) + k·σ_total）
        //   简化只取 η^(1/7) 项：粘度对残留是亚线性依赖，η 10 倍 ≈ 残留 1.39 倍
        // η = 按体积加权平均的各成分真实动态粘度（20°C，水 = 1）；
        // 未知成分（含 mod 新液体）默认 = 1
        // 实际残留 R = min(残留体积, 残留上限)，上限 0 = 不设上限
        // 倾倒/转移/清空时，出液容器最多只会失去 (液体量 − R)，
        // 也就是说：容器永远保留 R 这么多液体，R 的组成与整瓶液体同比例。
        // 当瓶里只剩 R 时再倒 → 转移量 0；注入新液体混合后，总量大于 R，
        // 才能再倒出 (总量 − R)。
        // 单位：游戏内部液体体积是 µl（500ml 瓶 = 500000；HeatLiquidOnStove
        // 的参数名就是 volumeUL），配置文件的 ml 值在进入计算前 ×1000。

        public static ResidueCapture Capture(GameItem source, GameItem target)
        {
            try
            {
                if (!ResiduePatches.MasterEnabled) return null;
                if (InternalOp) return null;
                if (source == null) return null;
                HardcoreLiquidSettings s = ProbablyHardcoreLiquidMod.Settings;
                if (s == null) return null;
                if (s.PercentValue <= 0f) return null; // 0 = 完全关闭
                if (!ScopeAllows(source)) return null;
                // 滴管不再豁免残留（v0.8.1 的免残留做法已被用户否掉）。
                // 滴管「吸不动」改由 DropperFix 在交互层解决：只剩残留时仍走吸取分支。
                int total = WaterHelper.GetTotalVolume(source);
                if (total <= 0) return null;
                int[] parts = WaterHelper.GetLiquidParts(source);
                if (parts == null || parts.Length == 0) return null;
                ResidueCapture c = new ResidueCapture();
                c.Source = source;
                c.Target = NormalizeTarget(target);
                c.TotalBefore = total;
                c.PartsBefore = parts;
                return c;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] capture failed: " + e.Message);
                return null;
            }
        }

        // 原版滴管标记（WaterHelper.InitLiquidContainerItem 里开在物品上的 tag）
        private const string DropperTag = "LIQUID_CONTAINER_DROPPER_TAG";

        public static bool IsDropper(GameItem item)
        {
            if (item == null) return false;
            try
            {
                return item.IsTag(DropperTag);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // 排水口/水槽/龙头不是"容器"，不做目标侧扣除（倒进它们的水本来就是销毁）
        private static GameItem NormalizeTarget(GameItem target)        {
            if (target == null) return null;
            try
            {
                if (WaterHelper.IsDrain(target)) return null;
                if (WaterHelper.IsFaucet(target)) return null;
            }
            catch (Exception) { }
            return target;
        }

        // ===== 容器范围：原版 / 全部 =====

        private static bool ScopeAllows(GameItem item)
        {
            string scope = ProbablyHardcoreLiquidMod.Settings.ScopeValue;
            if (scope == "all") return true;
            if (scope == "vanilla") return !IsModContainer(item);
            return true; // 未知值一律放行
        }

        private static bool IsModContainer(GameItem item)
        {
            try
            {
                string id = item != null ? item.identifier : null;
                if (string.IsNullOrEmpty(id)) return false;
                if (_modDir == null)
                {
                    _modDir = UnityEngine.Object.FindObjectOfType<ModItemDirectory>();
                    if (_modDir == null && !_modDirWarned)
                    {
                        _modDirWarned = true;
                        Debug.LogWarning("[HardcoreLiquid] ModItemDirectory not found yet (vanilla scope check will treat items as vanilla)");
                    }
                }
                if (_modDir == null) return false;
                return _modDir.Has(id);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] scope check failed: " + e.Message);
                return false;
            }
        }

        // ===== 残留结算 =====

        public static void Apply(ResidueCapture c)
        {
            try
            {
                if (c == null || c.Source == null) return;
                HardcoreLiquidSettings s = ProbablyHardcoreLiquidMod.Settings;
                if (s == null) return;

                GameItem source = c.Source;
                int after = WaterHelper.GetTotalVolume(source);

                // 容器固有残留 R（µl）。按"最大容积 × 比例 × 混合粘度"计算，上限封顶。
                long residueUl = ComputeResidueUl(source, c.TotalBefore, c.PartsBefore, s);
                if (residueUl <= 0) return;

                // 本事件最多只能保留"倒之前的总量"这么多残留
                long eff = residueUl;
                if (eff > c.TotalBefore) eff = c.TotalBefore;

                if (after >= eff) return; // 原版已经留下 ≥ 残留的量（比如目标瓶装满了），不用补

                long addBack = eff - after;

                InternalOp = true;
                try
                {
                    // 1) 源容器补回残留：组成 = 倒出前整瓶液体的比例
                    AddParts(source, c.PartsBefore, (int)addBack);
                    // 2) 目标容器拿走多转移的部分：转移量应 = 液体量 − 实际残留
                    RemoveFromTarget(c.Target, (int)addBack);
                }
                finally
                {
                    InternalOp = false;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] apply failed: " + e.Message);
                InternalOp = false;
            }
        }

        // ===== 粘度系数（v0.6，v0.6.7 起可逐项配置）=====
        // 以水 = 1 为基准；粘度表现在是配置项（设置菜单"粘度"分组，默认 = 20°C 真实
        // 动态粘度）；没列出的液体成分默认 = 1，其他 mod 引入的新液体自动有粘度系数。
        // 混合粘度 = 对数混合（Arrhenius / Grünberg-Nissan）：ln η = Σ(体积ᵢ·ln ηᵢ) / 总体积。
        // 有效粘度 = 1 + (混合粘度^(1/7) − 1) × viscosity_scale（scale=0 关闭粘度）。

        private static float ViscosityOf(string liquidId)
        {
            if (string.IsNullOrEmpty(liquidId)) return 1f;
            HardcoreLiquidSettings s = ProbablyHardcoreLiquidMod.Settings;
            if (s == null) return 1f;
            try
            {
                return s.GetViscosity(liquidId);
            }
            catch (Exception)
            {
                return 1f; // 任何异常 → 按水算
            }
        }

        // v0.9.10：对数混合（Arrhenius / Grünberg-Nissan）——ln η_mix = Σ(xᵢ·ln ηᵢ)
        // 粘度是乘性量，不是可加量：水 50% + 油脂 50% 得 8.94（线性平均会给 40.5，高估 4.5 倍）。
        // 浮点版供清洗积分使用（组成连续变化，不能用整数 µl）。
        internal static float MixViscosity(double[] parts)
        {
            List<Liquid> liquids = Liquid.Liquids;
            if (parts == null || liquids == null) return 1f;
            int n = parts.Length;
            if (n > liquids.Count) n = liquids.Count;
            double total = 0.0;
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double p = parts[i];
                if (p <= 0.0) continue;
                double eta = (double)ViscosityOf(liquids[i].identifier);
                if (eta < 0.01) eta = 0.01; // 防 log(0)：极稀组分按 0.01 处理
                total += p;
                sum += p * System.Math.Log(eta);
            }
            if (total <= 0.0) return 1f;
            return (float)System.Math.Exp(sum / total);
        }

        // 有效粘度因子，浮点版；与整数版同一公式，供清洗过程逐步积分使用
        public static double ViscosityFactor(double[] parts)
        {
            HardcoreLiquidSettings s = ProbablyHardcoreLiquidMod.Settings;
            if (s == null) return 1.0;
            float scale = s.ViscosityScaleValue;
            if (scale < 0f) scale = 0f;
            float mix = MixViscosity(parts);
            if (mix < 0f) mix = 0f;
            double v = 1.0 + (System.Math.Pow((double)mix, 1.0 / 7.0) - 1.0) * (double)scale;
            if (v < 0.05) v = 0.05;
            return v;
        }

        // 对数混合，整数版：转成 double 后走同一个实现，保证两处口径完全一致
        // （parts 的下标与 Liquid.Liquids 对齐）
        internal static float MixViscosity(int[] parts)
        {
            if (parts == null) return 1f;
            double[] d = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++) d[i] = parts[i];
            return MixViscosity(d);
        }

        // 有效粘度因子（1 = 按水算）；喷壶判定"内容物 vs 残留"时也用它，保证两处口径一致
        public static double ViscosityFactor(int[] parts)
        {
            HardcoreLiquidSettings s = ProbablyHardcoreLiquidMod.Settings;
            if (s == null) return 1.0;
            float scale = s.ViscosityScaleValue;
            if (scale < 0f) scale = 0f;
            float mix = MixViscosity(parts);
            if (mix < 0f) mix = 0f;
            double v = 1.0 + (System.Math.Pow((double)mix, 1.0 / 7.0) - 1.0) * (double)scale;
            if (v < 0.05) v = 0.05;
            return v;
        }

        // 残留体积 = 容器最大容积(ml) × 比例% × 有效粘度；再与上限(ml)取小，换算回 µl。
        private static long ComputeResidueUl(GameItem item, int totalUl, int[] parts, HardcoreLiquidSettings s)
        {
            float percent = s.PercentValue;
            if (percent <= 0f) return 0;

            // 容器最大容积（ml）。拿不到就用当前容量兜底（ml）。
            double capacityMl = 0.0;
            try
            {
                capacityMl = WaterHelper.GetTotalCapacityML(item);
            }
            catch (Exception) { }
            if (capacityMl <= 0.0)
            {
                capacityMl = totalUl / 1000.0;
            }
            if (capacityMl <= 0.0) return 0;

            // 有效因子 = 1 + (η^(1/7) − 1) × 缩放；缩放 0 = 关闭粘度（全部按水算）。
            // 采用药品灌装 HUV 经验模型的 η^(1/7) 项：粘度对残留是亚线性依赖
            // （η 10 倍 ≈ 残留 1.39 倍），油脂 80 倍粘度只给 80^(1/7)≈1.87 倍残留。
            double viscosity = ViscosityFactor(parts);

            double residueMl = capacityMl * (percent / 100.0) * viscosity;
            long capUl = s.MaxUlValue;
            if (capUl > 0 && residueMl * 1000.0 > capUl)
            {
                residueMl = capUl / 1000.0;
            }
            if (residueMl <= 0.0) return 0;
            return (long)Math.Round(residueMl * 1000.0, MidpointRounding.AwayFromZero);
        }

        // 目标容器按比例扣掉 addBack：我们只转移 (液体量 − 残留) 过去，
        // 原版把整瓶都转过去了，所以要把多转的部分从目标里减回来。
        // v0.5.1：这里必须用 WaterHelper.Remove（参数是 part=µl 单位），
        // 不能用 RemoveMl——RemoveMl 的参数是 ml，传入 µl 值会 1000× 过量扣除
        // （500ml 瓶倒 5ml 残留会把 495ml 的目标瓶整个扣空）。
        private static void RemoveFromTarget(GameItem target, int addBack)
        {
            try
            {
                if (target == null || addBack <= 0) return;
                int tAfter = WaterHelper.GetTotalVolume(target);
                if (tAfter < addBack) return; // 目标里都不够扣（理论上不会发生），保守跳过
                WaterHelper.Remove(target, addBack);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] target correction failed: " + e.Message);
            }
        }

        private static bool AddParts(GameItem item, int[] parts, int amountUl)
        {
            if (amountUl <= 0) return true;
            int[] dist = WaterHelper.SplitProportionally(parts, amountUl);
            if (dist == null) return false;
            List<Liquid> liquids = Liquid.Liquids;
            if (liquids == null) return false;
            int n = dist.Length;
            if (n > liquids.Count) n = liquids.Count;
            for (int i = 0; i < n; i++)
            {
                if (dist[i] <= 0) continue;
                string id = liquids[i].identifier;
                if (string.IsNullOrEmpty(id)) continue;
                WaterHelper.AddLiquid(item, id, dist[i]);
            }
            return true;
        }
    }
}
