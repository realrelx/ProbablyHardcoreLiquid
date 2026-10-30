using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyHardcoreLiquid
{
    public sealed class HardcoreLiquidSettings
    {
        public ModConfig Config;
        public string FilePath;
        // v0.6.4：残留模型 = "容器固有残留 × 粘度因子"——
        //   残留体积 = 容器最大容积 × 比例（百分数）× 粘度因子
        //   粘度因子 = 1 + (η^(1/7) − 1) × viscosity_scale
        //   （药品灌装 HUV 经验模型：滞留体积 V_HU = V_HU,water × η^(1/7) + k·σ，
        //   这里简化只取 η^(1/7) 项——粘度 10 倍只让残留约 1.39 倍，亚线性）
        //   η = 按体积加权的各液体成分真实动态粘度（20°C，水 = 1）；未知成分默认 = 1
        //   实际残留 = min(残留体积, 残留上限)；上限=0 表示不设上限
        //   单位说明：游戏内部液体体积单位是 µl（我们实测：500ml 瓶 = 500000，
        //   HeatLiquidOnStove 的参数名就叫 volumeUL）。配置文件里的数值仍按
        //   人类习惯的 ml 标注，代码里换算 ×1000。
        public ConfigEntry<float> ResiduePercent;
        public ConfigEntry<int> MaxResidueMl;
        public ConfigEntry<string> ContainerScope;
        public ConfigEntry<float> ViscosityScale;

        // ===== 粘度系数表（v0.6.7 起可逐项配置）=====
        // 覆盖原版全部 37 种液体成分（Liquid 类的 LIQUID_* 常量），
        // 默认值 = 真实动态粘度（20°C，以水 = 1.0 为基准），来源为化工手册/
        // 工程数据（工程工具箱、Solvay 烧碱数据表、Pavlov 化工手册等）。
        // 同义/别名键（游戏内可能有多种写法）映射到同一个配置项。
        private static readonly string[] ViscosityIds = {
            "water", "alcohol", "wine_extract", "fanta", "caffeine", "coffee_mush",
            "acid", "base", "chlorine", "zyanide", "chemical_contaminant", "physical_contaminant",
            "heavy_metal", "microplastic", "mineral", "organic_waste", "debris", "plant_matter",
            "microbe", "protein", "fat", "starch", "sugar", "dream_cap_mush", "dream_cap_extract",
            "nightmare_cap_mush", "nightmare_extract", "lipobond_agent", "synaprest", "nomore",
            "methylol", "reducing_agent", "stabilizing_agent", "contraceptive", "livestock_immunity",
            "livestock_steroid", "luck"
        };
        private static readonly string[] ViscosityNamesZh = {
            "水", "酒精（乙醇）", "葡萄酒提取物", "汽水", "咖啡因", "咖啡渣浆",
            "酸（浓盐酸）", "碱（烧碱）", "氯水", "氰化物", "化学污染物（柴油类）", "颗粒污染物",
            "重金属（浓盐水）", "微塑料", "矿物质（硬水）", "有机废料（活性污泥）", "不溶性残渣（淤泥）", "植物残渣（植物浆）",
            "微生物（菌液）", "蛋白质（蛋清类）", "油脂（烹饪油）", "淀粉（淀粉糊）", "糖（60% 蔗糖液）", "梦盖糊", "梦盖提取物",
            "梦魇盖糊", "梦魇提取物", "脂粘剂（卵磷脂类）", "斯那普雷", "诺莫尔",
            "甲醇类", "还原剂（水合肼类）", "稳定剂（黄原胶类）", "避孕激素", "牲畜免疫素（油佐剂疫苗）",
            "牲畜类固醇（油性注射）", "幸运"
        };
        private static readonly string[] ViscosityNamesEn = {
            "Water", "Alcohol (ethanol)", "Wine extract", "Soda", "Caffeine", "Coffee mush",
            "Acid (conc. HCl)", "Base (lye)", "Chlorine water", "Cyanide", "Chemical contaminant (diesel)", "Particulate contaminant",
            "Heavy metal (brine)", "Microplastic", "Mineral (hard water)", "Organic waste (sludge)", "Insoluble debris", "Plant matter (pulp)",
            "Microbes (culture)", "Protein (egg white)", "Fat (cooking oil)", "Starch (slurry)", "Sugar (60% syrup)", "Dream cap mush", "Dream cap extract",
            "Nightmare cap mush", "Nightmare extract", "Lipobond agent (lecithin)", "Synaprest", "NoMore",
            "Methylol (methanol)", "Reducing agent (hydrazine)", "Stabilizing agent (xanthan)", "Contraceptive hormones", "Livestock immunity (adjuvanted vaccine)",
            "Livestock steroid (oily)", "Luck"
        };
        private static readonly float[] ViscosityDefaults = {
            1.0f, 1.2f, 1.5f, 1.5f, 1.0f, 5.0f,
            2.0f, 13.0f, 1.0f, 1.1f, 3.0f, 1.3f,
            1.5f, 1.3f, 1.2f, 5.0f, 4.0f, 5.0f,
            3.0f, 4.0f, 80.0f, 100.0f, 60.0f, 6.0f, 1.8f,
            7.0f, 2.0f, 100.0f, 1.2f, 1.2f,
            0.7f, 0.9f, 10.0f, 1.5f, 10.0f,
            15.0f, 1.2f
        };
        private static readonly Dictionary<string, string> ViscosityAliases =
            new Dictionary<string, string>()
        {
            { "coffee_bean_mush", "coffee_mush" },
            { "fat_oil", "fat" },
            { "insoluble_debris", "debris" },
            { "microbes", "microbe" },
            { "contraceptive_hormones", "contraceptive" },
            { "n_methylol_base", "methylol" }
        };
        public Dictionary<string, ConfigEntry<float>> ViscosityEntries;

        public void Init(string modId)
        {
            try
            {
                Config = ModHelper.GetConfig(modId);
                if (Config == null)
                {
                    Config = ModConfig.For(modId);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] GetConfig failed: " + e.Message);
                Config = null;
            }
            if (Config == null)
            {
                Debug.LogWarning("[HardcoreLiquid] ModConfig unavailable, falling back to built-in defaults");
                return;
            }
            try
            {
                FilePath = Config.FilePath;
                ResiduePercent = Config.Define("residue_percent", 0.5f, "residue",
                    delegate() { return Loc.T("残留比例 (%)", "Residue percent (%)"); },
                    null, null,
                    delegate() { return Loc.T("容器最大容积乘这个百分数得到残留体积：1 = 1%，0.5 = 0.5%，0 = 完全关闭", "Residue volume = container capacity x this percent: 1 = 1%, 0.5 = 0.5%, 0 = disabled"); },
                    false);
                MaxResidueMl = Config.Define("max_residue_ml", 10, "residue",
                    delegate() { return Loc.T("残留上限 (ml)", "Residue cap (ml)"); },
                    null, null,
                    delegate() { return Loc.T("实际残留 = min(残留体积, 残留上限)，防止超大容器留下过高残留；0 = 不设上限", "Actual residue = min(residue volume, cap), prevents huge tanks keeping too much; 0 = no cap"); },
                    false);
                ContainerScope = Config.Define("container_scope", "all", "residue",
                    delegate() { return Loc.T("容器范围", "Container scope"); },
                    new string[] { "all", "vanilla" },
                    delegate() { return Loc.T("哪些容器会残留：all = 全部（含 mod 容器）；vanilla = 仅原版容器", "Which containers keep residue: all = everything (incl. mod items); vanilla = vanilla only"); },
                    false);
                ViscosityScale = Config.Define("viscosity_scale", 1f, "residue",
                    delegate() { return Loc.T("粘度系数缩放", "Viscosity scaling"); },
                    null, null,
                    delegate() { return Loc.T("液体越粘残留越多（亚线性：粘度 10 倍 ≈ 残留 1.39 倍）。粘度因子 = 1 + (η^(1/7) − 1) × 缩放：1 = 按内置真实粘度表（水=1），0 = 关闭粘度（全部按水算）", "Thicker liquids leave more (sub-linear: 10x viscosity = ~1.39x residue). Factor = 1 + (n^(1/7) - 1) x scale: 1 = built-in real viscosity table (water = 1), 0 = viscosity off (everything counts as water)"); },
                    false);

                // 粘度系数表：37 种成分逐项可调，默认 = 真实物理值（20°C，水 = 1）。
                // 数值项不传 min/max → 设置菜单渲染为自由输入框。
                ViscosityEntries = new Dictionary<string, ConfigEntry<float>>();
                for (int i = 0; i < ViscosityIds.Length; i++)
                {
                    string id = ViscosityIds[i];
                    string zhName = ViscosityNamesZh[i];
                    string enName = ViscosityNamesEn[i];
                    float def = ViscosityDefaults[i];
                    ViscosityEntries[id] = Config.Define("viscosity_" + id, def, "viscosity",
                        delegate() { return Loc.T(zhName, enName); },
                        null, null,
                        delegate() { return Loc.T("该液体成分的粘度系数（水 = 1）：数值越大残留越多。默认值为 20°C 真实动态粘度。", "Viscosity factor of this liquid component (water = 1): higher means more residue. Default is the real dynamic viscosity at 20C."); },
                        false);
                }
                Debug.Log("[HardcoreLiquid] settings ready, config=" + (FilePath == null ? "(unknown)" : FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] Define/Load failed: " + e.Message);
            }
        }

        // 配置项缺失时的安全默认值
        public float PercentValue { get { return ResiduePercent != null ? ResiduePercent.Value : 0.5f; } }
        public int MaxMlValue { get { return MaxResidueMl != null ? MaxResidueMl.Value : 10; } }
        public string ScopeValue { get { return ContainerScope != null && !string.IsNullOrEmpty(ContainerScope.Value) ? ContainerScope.Value : "all"; } }
        public float ViscosityScaleValue { get { return ViscosityScale != null ? ViscosityScale.Value : 1f; } }

        // 取某液体成分的粘度系数：别名先映射到主键；未配置/未知成分（含 mod 新液体）默认 = 1
        public float GetViscosity(string liquidId)
        {
            if (string.IsNullOrEmpty(liquidId)) return 1f;
            string key = liquidId;
            string alias;
            if (ViscosityAliases.TryGetValue(liquidId, out alias)) key = alias;
            ConfigEntry<float> entry;
            if (ViscosityEntries == null || !ViscosityEntries.TryGetValue(key, out entry) || entry == null) return 1f;
            try
            {
                float v = entry.Value;
                if (v < 0f) return 0f;
                return v;
            }
            catch (Exception)
            {
                return 1f;
            }
        }

        // 上限换算成游戏内部单位 µl（配置文件是 ml）
        public long MaxUlValue
        {
            get
            {
                long ml = MaxMlValue;
                if (ml <= 0) return 0; // 0/负数 = 不设上限
                if (ml > 2000000L) return 2000000000000L; // 防溢出，实际容器到不了这个量
                return ml * 1000L;
            }
        }

        public void Reload()
        {
            // ModConfig.Load 不是公开 API；配置加载由游戏侧管理
            //（Mods 界面应用配置时游戏会触发 OnModConfigApplied）。
            Debug.Log("[HardcoreLiquid] config reload requested (game-managed)");
        }

        public void Save()
        {
            try
            {
                if (Config != null) Config.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] save failed: " + e.Message);
            }
        }
    }
}
