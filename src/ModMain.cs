using System;
using UnityEngine;
using HarmonyLib;

namespace ProbablyHardcoreLiquid
{
    public sealed class ProbablyHardcoreLiquidMod : IMod
    {
        public static HardcoreLiquidSettings Settings;

        private Harmony _harmony;
        private string _modId = "ProbablyHardcoreLiquid";

        public void Init(ModManifest manifest)
        {
            try
            {
                if (manifest != null && !string.IsNullOrEmpty(manifest.ID))
                {
                    _modId = manifest.ID;
                }
                Settings = new HardcoreLiquidSettings();
                Settings.Init(_modId);
                RegisterConfigHook();
                RegisterDebugFunction();
                Debug.Log("[HardcoreLiquid] initialized, id=" + _modId);
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
            if (_modId == modId && Settings != null)
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
                ModHelper.AddDebugFunction(_modId, "HardcoreLiquid", "hardcore_residue_status", PrintStatus, false);
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
                + " config=" + (Settings.FilePath == null ? "(unknown)" : Settings.FilePath));
        }
    }
}
