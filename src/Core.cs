using System;
using UnityEngine;
using HarmonyLib;

namespace ProbablyHardcoreLiquid
{
    public sealed class ProbablyHardcoreLiquidMod : IMod
    {
        public static HardcoreLiquidSettings Settings;
        public static string ModId = "ProbablyHardcoreLiquid";

        private Harmony _harmony;

        public void Init(ModManifest manifest)
        {
            try
            {
                if (manifest != null && !string.IsNullOrEmpty(manifest.ID))
                {
                    ModId = manifest.ID;
                }
                Settings = new HardcoreLiquidSettings();
                Settings.Init(ModId);
                RegisterConfigHook();
                RegisterDebugFunction();
                Debug.Log("[HardcoreLiquid] initialized, id=" + ModId);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] Init failed: " + e);
            }
        }

        public void OnEnable()
        {
            try
            {
                if (_harmony == null)
                {
                    _harmony = new Harmony("probably.hardcore.liquid");
                    ResiduePatches.Apply(_harmony);
                }
                ResiduePatches.MasterEnabled = true;
                ModHook.OnHandlingNightlyServicesEarly += ResiduePatches.OnNightlyServicesEarly;
                ModHook.OnHandlingNightlyServicesLate += ResiduePatches.OnNightlyServicesLate;
                ModHook.OnModItemDirectoryInit += OnModItemDirectoryInit;
                ModHook.OnNewGameStarted += OnNewGameStarted;
                ModHook.OnGameLoadedInit += OnGameLoadedInit;
                ModHook.OnGameLoadedLate += OnGameLoadedLate;
                ModHook.OnShutterOpenedEarly += OnShutterOpenedEarlyHook;
                ModHook.OnPlaceInventorInventoryItemEarly += OnPlaceInventorInventoryItemEarly;
                Debug.Log("[HardcoreLiquid] enabled");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] OnEnable failed: " + e);
            }
        }

        public void OnDisable()
        {
            try
            {
                ResiduePatches.MasterEnabled = false;
                ModHook.OnHandlingNightlyServicesEarly -= ResiduePatches.OnNightlyServicesEarly;
                ModHook.OnHandlingNightlyServicesLate -= ResiduePatches.OnNightlyServicesLate;
                ModHook.OnModItemDirectoryInit -= OnModItemDirectoryInit;
                ModHook.OnNewGameStarted -= OnNewGameStarted;
                ModHook.OnGameLoadedInit -= OnGameLoadedInit;
                ModHook.OnGameLoadedLate -= OnGameLoadedLate;
                ModHook.OnShutterOpenedEarly -= OnShutterOpenedEarlyHook;
                ModHook.OnPlaceInventorInventoryItemEarly -= OnPlaceInventorInventoryItemEarly;
                // 注意：故意不调用 UnpatchAll()——游戏内置的模组扫描器把对
                // UnpatchAll 的调用视为高危（unpatch-all hard flag）直接拒绝加载。
                // 禁用时只关主开关，patch 保留但完全不生效。
                Debug.Log("[HardcoreLiquid] disabled");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] OnDisable failed: " + e);
            }
        }

        // ===== 喷壶物品注册与货源 =====
        private void OnModItemDirectoryInit(ModItemDirectory dir)
        {
            try
            {
                Spray.Register(dir);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] item directory init failed: " + e.Message);
            }
        }

        // 注意：OnNewGameStarted 的签名是 Action<String>（不是无参 Action）
        private void OnNewGameStarted(string arg)
        {
            try
            {
                // v0.8.8：_giftGiven / _pendingItem 是进程级静态字段，
                // 同一会话里连开几个新档时第二个档起就被这个标记挡住 → 每个新档都要先复位。
                Spray.ResetGiftState();
                Spray.GiftIfChemist();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] new game hook failed: " + e.Message);
            }
        }

        // 博士（Inventor）摆库存时刻：官方通道给货架加喷壶（仿移液枪 mod）
        private void OnPlaceInventorInventoryItemEarly(System.Collections.Generic.List<GameItem> list)
        {
            Spray.AddToDoctorStock(list);
        }

        private void OnGameLoadedInit()
        {
            try
            {
                Spray.GiftIfChemist(); // 旧档/新开都试一次；内部 _giftGiven 一次性
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] game loaded hook failed: " + e.Message);
            }
        }

        private void OnGameLoadedLate()
        {
            // 化学商人货源改为 patch StoreClientListBarter.CreateLowerLevelChemistBarter
            // （见 ResiduePatches.P_ChemistBarter），不再需要启动时播种。
            // v0.8.3：读档完成后再试一次开局赠壶的落位（此时库存容器已就绪）。
            Spray.TryGiftPending();
        }

        // 开张那一刻再补一次：有的存档第一次读档时库存窗口还没建好
        private void OnShutterOpenedEarlyHook()
        {
            try
            {
                Spray.TryGiftPending();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] shutter hook failed: " + e.Message);
            }
        }

        private void RegisterConfigHook()
        {
            try
            {
                ModHook.OnModConfigApplied += OnModConfigApplied;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] config hook failed: " + e.Message);
            }
        }

        private void OnModConfigApplied(string modId)
        {
            if (ModId == modId && Settings != null)
            {
                Settings.Reload();
            }
        }

        // 设置菜单说明：游戏的 mod 设置窗口（Mods 列表 → 设置）会依据
        // ModConfig.Define 的条目自动渲染——数值项没有 min/max 时显示自由输入框，
        // container_scope 有 choices 时显示下拉框。因此这里不再手动往
        // GameOptionRegistry 注册任何选项（避免与自动渲染重复造成菜单异常）。
        // 配置文件仍是权威来源：ModConfigs\ProbablyHardcoreLiquid.json。

        private void RegisterDebugFunction()
        {
            try
            {
                ModHelper.AddDebugFunction(ModId, "HardcoreLiquid", "hardcore_residue_status", PrintStatus, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] debug function failed: " + e.Message);
            }
        }

        private void PrintStatus()
        {
            Debug.Log("[HardcoreLiquid] percent=" + Settings.PercentValue
                + "% maxMl=" + Settings.MaxMlValue
                + " scope=" + Settings.ScopeValue
                + " viscosityScale=" + Settings.ViscosityScaleValue
                + " spray=" + (Settings.SprayEnabledValue ? "on" : "off")
                + " rinseRatio=" + Settings.SprayRinseRatioValue
                + " giftFill=" + Settings.SprayGiftFillValue
                + " vendorWeight=" + Settings.SprayChemistWeightValue
                + " doctorProb=" + Settings.SprayDoctorProbValue
                + " price=" + Settings.SprayPriceValue
                + " config=" + (Settings.FilePath == null ? "(unknown)" : Settings.FilePath));
        }
    }
}
