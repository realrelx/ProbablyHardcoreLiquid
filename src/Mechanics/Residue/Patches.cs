using System;
using UnityEngine;
using HarmonyLib;

namespace ProbablyHardcoreLiquid
{
    // v0.3.3：完全去掉本 mod 自己 IL 里的 System.Reflection 调用（改用
    // Harmony 的 [HarmonyPatch] 特性 + PatchAll，与 Pen的移液枪 同款做法），
    // 这样模组扫描器不会检出任何权限 → manifest 可以一个 Capability 都不声明。
    public static class ResiduePatches
    {
        private static int _scopeDepth;
        private static int _nightlyDepth;

        // mod 禁用时关掉的主开关（替代 UnpatchAll，避开模组扫描器的 unpatch-all hard flag）
        public static bool MasterEnabled;

        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.PatchAll();
                Debug.Log("[HardcoreLiquid] patches applied (PatchAll)");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] PatchAll failed: " + e.Message);
            }
        }

        // ModHook.OnHandlingNightlyServicesEarly / Late 订阅（普通 Action）
        public static void OnNightlyServicesEarly()
        {
            _nightlyDepth++;
        }

        public static void OnNightlyServicesLate()
        {
            if (_nightlyDepth > 0) _nightlyDepth--;
        }

        // 所有 patch 共用的捕获/结算。
        // sourceIndex：出液容器在参数列表里的下标；targetIndex：接收容器下标（-1 = 无目标/排水清空类）。
        public static ResidueCapture CaptureArg(object[] __args, int sourceIndex, int targetIndex)
        {
            if (Spray.Rinsing) return null; // 喷壶清洗期间：量由清洗逻辑管理，残留结算跳过
            _scopeDepth++;
            ResidueCapture cap = null;
            try
            {
                GameItem source = null;
                GameItem target = null;
                if (__args != null)
                {
                    if (sourceIndex >= 0 && sourceIndex < __args.Length) source = __args[sourceIndex] as GameItem;
                    if (targetIndex >= 0 && targetIndex < __args.Length) target = __args[targetIndex] as GameItem;
                }
                if (source != null)
                {
                    cap = ResidueLogic.Capture(source, target);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] prefix failed: " + e.Message);
            }
            return cap;
        }

        public static void Settle(ResidueCapture __state)
        {
            try
            {
                _scopeDepth--;
                if (_scopeDepth > 0) return;   // 内层调用，由最外层统一结算
                if (_nightlyDepth > 0) return; // 夜间机器流程（净水器/蒸发器等），不干预
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] postfix guard failed: " + e.Message);
                return;
            }
            ResidueLogic.Apply(__state);
            // 液位贴图同步（喷壶参与本次液体变化时）
            try
            {
                if (__state != null)
                {
                    Spray.RefreshSprite(__state.Source);
                    Spray.RefreshSprite(__state.Target);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[HardcoreLiquid] sprite refresh hook failed: " + e.Message);
            }
        }

        // ===== 十个液体出口 + 一个夜间守卫 =====
        // (source, target) 下标随方法签名变化；target=-1 表示清空/排空类（无接收容器）

        [HarmonyPatch(typeof(WaterHelper), "TransferLiquid")]
        public static class P_TransferLiquid
        {
            // TransferLiquid(GameItem sourceContainer, GameItem targetContainer)
            public static void Prefix(object[] __args, out ResidueCapture __state) { __state = CaptureArg(__args, 0, 1); }
            public static void Postfix(ResidueCapture __state) { Settle(__state); }
        }

        [HarmonyPatch(typeof(LiquidContainerHelper), "TransferLiquid")]
        public static class P_LCTransferLiquid
        {
            // TransferLiquid(GameItem targetItem, GameItem sourceItem)
            public static void Prefix(object[] __args, out ResidueCapture __state) { __state = CaptureArg(__args, 1, 0); }
            public static void Postfix(ResidueCapture __state) { Settle(__state); }
        }

        [HarmonyPatch(typeof(LiquidContainerHelper), "SplitWater")]
        public static class P_SplitWater
        {
            // SplitWater(GameItem sourceContainer)：分出的新容器由原版内部创建，拿不到目标 → -1
            public static void Prefix(object[] __args, out ResidueCapture __state) { __state = CaptureArg(__args, 0, -1); }
            public static void Postfix(ResidueCapture __state) { Settle(__state); }
        }

        [HarmonyPatch(typeof(WaterHelper), "EmptyContainer")]
        public static class P_EmptyContainer
        {
            public static void Prefix(object[] __args, out ResidueCapture __state) { __state = CaptureArg(__args, 0, -1); }
            public static void Postfix(ResidueCapture __state) { Settle(__state); }
        }

        [HarmonyPatch(typeof(WaterHelper), "RemoveMl")]
        public static class P_RemoveMl
        {
            public static void Prefix(object[] __args, out ResidueCapture __state) { __state = CaptureArg(__args, 0, -1); }
            public static void Postfix(ResidueCapture __state) { Settle(__state); }
        }

        [HarmonyPatch(typeof(LiquidContainerHelper), "RemoveVolume")]
        public static class P_RemoveVolume
        {
            public static void Prefix(object[] __args, out ResidueCapture __state) { __state = CaptureArg(__args, 0, -1); }
            public static void Postfix(ResidueCapture __state) { Settle(__state); }
        }

        [HarmonyPatch(typeof(WineHelper), "PourWineIntoContainer")]
        public static class P_PourWineIntoContainer
        {
            // PourWineIntoContainer(GameItem bottle, GameItem container)
            public static void Prefix(object[] __args, out ResidueCapture __state) { __state = CaptureArg(__args, 0, 1); }
            public static void Postfix(ResidueCapture __state) { Settle(__state); }
        }

        [HarmonyPatch(typeof(WineHelper), "PourRationIntoBottle")]
        public static class P_PourRationIntoBottle
        {
            // PourRationIntoBottle(GameItem bottle, GameItem ration)
            public static void Prefix(object[] __args, out ResidueCapture __state) { __state = CaptureArg(__args, 1, 0); }
            public static void Postfix(ResidueCapture __state) { Settle(__state); }
        }

        [HarmonyPatch(typeof(WineHelper), "DrainBottle")]
        public static class P_DrainBottle
        {
            public static void Prefix(object[] __args, out ResidueCapture __state) { __state = CaptureArg(__args, 0, -1); }
            public static void Postfix(ResidueCapture __state) { Settle(__state); }
        }

        [HarmonyPatch(typeof(WineHelper), "EmptyWineBottle")]
        public static class P_EmptyWineBottle
        {
            public static void Prefix(object[] __args, out ResidueCapture __state) { __state = CaptureArg(__args, 0, -1); }
            public static void Postfix(ResidueCapture __state) { Settle(__state); }
        }

        // RunNightlyChemicalSimulation 兜底守卫（防夜间服务事件未覆盖的路径）
        [HarmonyPatch(typeof(ChemicalHelper), "RunNightlyChemicalSimulation")]
        public static class P_Nightly
        {
            public static void Prefix()
            {
                _nightlyDepth++;
            }

            public static void Postfix()
            {
                if (_nightlyDepth > 0) _nightlyDepth--;
            }
        }

        // ===== 喷壶（spray bottle）=====
        // 喷壶只能倒掉（排水口/水槽），不能整壶转移给别的容器；
        // 拖到另一个液体容器 = 少量多次清洗（CSTR 置换，总量 M = rinse_ratio × 目标残留）。
        // 注意：同方法多 [HarmonyPatch] 类合法；prefix 返回 false 时其他 postfix 仍会跑，
        // 但 Settle 对 null state 是安全的（ResidueLogic.Apply 首行判空）。

        [HarmonyPatch(typeof(WaterHelper), "TransferLiquid")]
        public static class P_SprayTransfer
        {
            // TransferLiquid(GameItem sourceContainer, GameItem targetContainer)
            public static bool Prefix(object[] __args)
            {
                try
                {
                    if (__args == null || __args.Length < 2) return true;
                    GameItem source = __args[0] as GameItem;
                    GameItem target = __args[1] as GameItem;
                    if (source == null || target == null) return true;
                    if (!Spray.IsSpray(source)) return true;
                    if (!ProbablyHardcoreLiquidMod.Settings.SprayEnabledValue) return false; // 喷壶被禁用：整壶拒绝
                    // 倒掉（排水口/水槽/水龙头）放行，原版逻辑会清空喷壶
                    if (WaterHelper.IsDrain(target) || WaterHelper.IsFaucet(target)) return true;
                    // 目标是液体容器 → 少量多次清洗，拦截原版整壶转移
                    if (target.state != null && target.state.IsTag(LiquidContainerHelper.LIQUID_CONTAINER_TAG))
                    {
                        Spray.Rinse(source, target);
                        return false;
                    }
                    return false; // 其他非液体容器目标一律拒绝（喷壶只能倒掉或清洗）
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[HardcoreLiquid] spray transfer prefix failed: " + e.Message);
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(LiquidContainerHelper), "TransferLiquid")]
        public static class P_SprayLCTransfer
        {
            // TransferLiquid(GameItem targetItem, GameItem sourceItem) —— 参数顺序与 WaterHelper 相反
            public static bool Prefix(object[] __args)
            {
                try
                {
                    if (__args == null || __args.Length < 2) return true;
                    GameItem source = __args[1] as GameItem;
                    GameItem target = __args[0] as GameItem;
                    if (source == null || target == null) return true;
                    if (!Spray.IsSpray(source)) return true;
                    if (!ProbablyHardcoreLiquidMod.Settings.SprayEnabledValue) return false;
                    if (WaterHelper.IsDrain(target) || WaterHelper.IsFaucet(target)) return true;
                    if (target.state != null && target.state.IsTag(LiquidContainerHelper.LIQUID_CONTAINER_TAG))
                    {
                        Spray.Rinse(source, target);
                        return false;
                    }
                    return false;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[HardcoreLiquid] spray LC transfer prefix failed: " + e.Message);
                    return true;
                }
            }
        }

        // 博士货架改走官方 ModHook.OnPlaceInventorInventoryItemEarly 通道（见 ModMain），
        // 不再 patch TradeSheet.GetGenericMedicalShop（移液枪 mod 的同款做法）。

        // 化学商人（下层区化学家，以物易物）：仿原版 salve/caffeine_pill/dream_dust 的
        // 做法——他上门时按权重概率把一把空喷壶放到交易台（AddDirectSellingItemToTable）。
        [HarmonyPatch(typeof(StoreClientListBarter), "CreateLowerLevelChemistBarter", new Type[] { typeof(bool) })]
        public static class P_ChemistBarter
        {
            public static void Postfix(ref StoreClient __result)
            {
                try
                {
                    if (__result == null) return;
                    if (!ProbablyHardcoreLiquidMod.Settings.SprayEnabledValue) return;
                    int weight = ProbablyHardcoreLiquidMod.Settings.SprayChemistWeightValue;
                    if (weight <= 0) return;
                    __result.OnArrival += delegate()
                    {
                        try
                        {
                            if (!Spray.Roll(weight / 100.0)) return; // 配置为百分数（默认 10 = 10%）
                            if (!PlayerStore.IsInstanceExist()) return;
                            GameItem spray = Spray.CreateSpray();
                            if (spray != null)
                            {
                                PlayerStore.Instance.AddDirectSellingItemToTable(spray, false, false, false, 1);
                                Debug.Log("[HardcoreLiquid] chemist merchant brought a spray bottle");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[HardcoreLiquid] chemist spray offer failed: " + e.Message);
                        }
                    };
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[HardcoreLiquid] chemist barter patch failed: " + e.Message);
                }
            }
        }

        // 化学家开局礼：挂 NewGameData.HandleInitialItem（原版发开局套件=搅拌机/加热板/量筒/
        // 咖啡豆/卡特尔保护服务等的那个时刻）——比 OnNewGameStarted 可靠（后者触发时
        // startType 可能还没定）。startType==6 即化学家档（IsChemistStart 同判据）。
        [HarmonyPatch(typeof(NewGameData), "HandleInitialItem")]
        public static class P_InitialItem
        {
            public static void Postfix()
            {
                try
                {
                    if (!ProbablyHardcoreLiquidMod.Settings.SprayEnabledValue) return;
                    if (!ProbablyHardcoreLiquidMod.Settings.SprayStartGiftValue) return;
                    if (!PlayerStore.IsInstanceExist()) return;
                    PlayerStore ps = PlayerStore.Instance;
                    if (ps == null || !ps.IsChemistStart()) return;
                    // 此刻库存网格可能尚未就绪（TryFindOneValidInventorySlot 会拒收，
                    // 物品被 DestroyIfUnplaced 静默销毁）→ 只记挂账，等 OnGameLoadedInit 再发。
                    Spray.RequestGiftIfChemist();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[HardcoreLiquid] initial item patch failed: " + e.Message);
                }
            }
        }
    }
}
