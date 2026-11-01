using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ProbablyHardcoreLiquid
{
    // v0.8.2：原版滴管只有「空 → 吸、非空 → 倒」两个分支（WaterHelper.InitLiquidContainerItem
    // 里挂的 b__9）。残留系统让滴管永远非空，于是永远只能往外倒——这就是「吸不动」。
    // 修法不是免掉残留（v0.8.1 那样做等于删掉玩法），而是在交互层加一个分叉：
    // 管内液体只剩残渣时，点液体容器仍然走「吸」，其余情况完全交还原版。
    [HarmonyPatch(typeof(WaterHelper), "InitLiquidContainerItem")]
    public static class DropperFixPatch
    {
        // InitLiquidContainerItem(GameItem item, int capacityInMl, int basePrice, bool isFilterContainer,
        //                         bool isGraduated, bool isDropper, bool isTransparentContainer, int sheetPage)
        public static void Postfix(object[] __args)
        {
            try
            {
                if (__args == null || __args.Length == 0) return;
                DropperFix.Install(__args[0] as GameItem);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] dropper install failed: " + e.Message);
            }
        }
    }

    public static class DropperFix
    {
        // 同一个物品只包一次（物品可能被 Repair 重建，CWT 随对象回收）
        private static readonly ConditionalWeakTable<GameItem, object> Installed =
            new ConditionalWeakTable<GameItem, object>();

        public static void Install(GameItem item)
        {
            if (item == null) return;
            if (!ResidueLogic.IsDropper(item)) return;

            object dummy;
            if (Installed.TryGetValue(item, out dummy)) return;
            Installed.Add(item, new object());

            Action<GameItem, GameItem> vanilla = item.onThisTargetItemFunc;

            item.onThisTargetItemFunc = delegate(GameItem self, GameItem target)
            {
                bool handled = false;
                try
                {
                    handled = TrySmartDraw(self, target);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[HardcoreLiquid] dropper smart-draw failed: " + e.Message);
                }
                if (handled) return;
                if (vanilla != null) vanilla(self, target);
            };

            Debug.Log("[HardcoreLiquid] dropper smart-draw installed: " + item.identifier);
        }

        // 只在「管内只剩残渣」时接管交互，返回 true 表示这次交互已经被处理掉
        public static bool TrySmartDraw(GameItem self, GameItem target)
        {
            if (self == null || target == null || self == target) return false;
            if (!ProbablyHardcoreLiquidMod.Settings.DropperSmartDrawValue) return false;
            if (!ResidueLogic.IsDropper(self)) return false;

            // 排水口 / 水龙头 / 非液体容器：不接管，原版逻辑照旧（倒掉、接水）
            if (!target.IsTag(LiquidContainerHelper.LIQUID_CONTAINER_TAG)) return false;
            if (!WaterHelper.CanUseWaterContainer(target)) return false;

            int capUl = WaterHelper.GetTotalCapacity(self);
            int volUl = WaterHelper.GetTotalVolume(self);
            if (capUl <= 0 || volUl <= 0 || volUl > capUl) return false;

            long ceiling = ResidueCeilingUl(capUl);
            if (volUl > ceiling) return false;   // 还有实质液体 → 交还原版「倒出」

            int targetVol = WaterHelper.GetTotalVolume(target);
            if (targetVol <= 0)
            {
                Debug.Log("[HardcoreLiquid] dropper draw skipped (target empty): vol=" + volUl);
                return true;   // 消费掉这次交互，避免把残渣倒进空容器
            }

            WaterHelper.TransferLiquid(target, self);

            int after = WaterHelper.GetTotalVolume(self);
            if (after > volUl)
            {
                try { AudioHelper.DropperDraw(); } catch (Exception) { }
                Debug.Log("[HardcoreLiquid] dropper smart-draw: " + volUl + " -> " + after
                          + " ul (ceiling " + ceiling + ")");
            }
            return true;
        }

        // 「只剩残渣」的判定上限 = 该容器的残留量（与 ResidueLogic 同一套参数）+ 一点余量，
        // 因为残留结算有取整，实际留在管里的量可能比理论值多几个 µl。
        private static long ResidueCeilingUl(int capUl)
        {
            HardcoreLiquidSettings s = ProbablyHardcoreLiquidMod.Settings;
            if (s == null) return 0;

            long residue = (long)(capUl * (double)s.PercentValue / 100.0);
            if (residue > s.MaxUlValue) residue = s.MaxUlValue;
            if (residue < 0) residue = 0;

            long slack = Math.Max(20L, residue / 2);
            return residue + slack;
        }
    }
}
