using HarmonyLib;
using Nicokobo.ContentInterop;
using UnityEngine;

namespace Nicokobo.AutomatedLogistics.Integration;

internal static class NativeHooks
{
    private static readonly List<HarmonyLib.Harmony> Harmonies = new();
    private static readonly MechanicLocalizedPatches WaterTransferPatch = new(Content.Owner, "native.water_transfer", PatchWaterTransfer);
    internal static bool UiAvailable { get; private set; }
    internal static Action<GameItem>? Open;
    internal static Action<GameItem, GameItem>? OpenMachine;
    internal static Action<GameItem>? Rename;
    internal static Action<string>? Log;
    internal static AutomatedLogisticsRuntime? Runtime;
    internal static Func<bool>? Visible, PointerInside, TextEditing;
    internal static Func<bool>? DismissPopup;
    internal static Action? Close;
    internal static readonly Nicokobo.ContentInterop.WindowInputCapture Capture = new();
    internal static bool MouseHeld => Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
    private static bool Blocks => Capture.BlocksPointer(Time.frameCount, PointerInside?.Invoke() == true, MouseHeld);
    internal static void Install()
    {
        // Keep the lookup types explicit: these adapters only address the game.
        var hooks = new[]
        {
            (Target: AccessTools.Method(typeof(GameItemElement), "GetTooltipBasic", Type.EmptyTypes), Callback: nameof(ItemTooltip), Postfix: true),
            (Target: AccessTools.Method(typeof(PowerHelper), nameof(PowerHelper.UpdateEnergyCreditSprite), new[] { typeof(GameItem) }), Callback: nameof(BatterySprite), Postfix: true),
            (Target: AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.BeginDay), Type.EmptyTypes), Callback: nameof(AfterBeginDay), Postfix: true),
            (Target: AccessTools.Method(typeof(GameItem), nameof(GameItem.MayActivateSlotItem), Type.EmptyTypes), Callback: nameof(CanActivate), Postfix: false),
            (Target: AccessTools.Method(typeof(GameItem), nameof(GameItem.CanActivateSlotItem), Type.EmptyTypes), Callback: nameof(CanActivate), Postfix: false),
            (Target: AccessTools.Method(typeof(GameItem), nameof(GameItem.ActivateSlotItem), Type.EmptyTypes), Callback: nameof(Activate), Postfix: false),
            (Target: AccessTools.Method(typeof(InputActionManager), "Update", Type.EmptyTypes), Callback: nameof(InputPrefix), Postfix: false),
            (Target: AccessTools.Method(typeof(CustomEventHandler), "Update", Type.EmptyTypes), Callback: nameof(DoubleClickPrefix), Postfix: false),
            (Target: AccessTools.Method(typeof(StoreUIManager), nameof(StoreUIManager.CheckRaycast), Type.EmptyTypes), Callback: nameof(RaycastPrefix), Postfix: false),
            (Target: AccessTools.Method(typeof(StoreUIManager), "Update", Type.EmptyTypes), Callback: nameof(StorePostfix), Postfix: true),
            (Target: AccessTools.Method(typeof(StoreUIManager), nameof(StoreUIManager.ResolveEscape), Type.EmptyTypes), Callback: nameof(Escape), Postfix: false),
            (Target: AccessTools.Method(typeof(StoreUIManager), nameof(StoreUIManager.CloseAllUI), Type.EmptyTypes), Callback: nameof(ClosePostfix), Postfix: true),
            (Target: AccessTools.Method(typeof(ItemContextHandler), "EqualizeSortButtonWidths", Type.EmptyTypes), Callback: nameof(GatherMenuWidths), Postfix: true),
            (Target: AccessTools.Method(typeof(StorageExpansionHelper), "GetKitTabs", new[] { typeof(GameItem) }), Callback: nameof(StorageKitTabs), Postfix: false),
            (Target: AccessTools.Method(typeof(StorageExpansionHelper), nameof(StorageExpansionHelper.Expand), new[] { typeof(GameItem), typeof(GameItem) }), Callback: nameof(StorageTabsChanged), Postfix: true),
            (Target: AccessTools.Method(typeof(StorageExpansionHelper), nameof(StorageExpansionHelper.RestoreTabs), new[] { typeof(GameItem) }), Callback: nameof(StorageTabsChanged), Postfix: true),
            (Target: AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.InitialSave), Type.EmptyTypes), Callback: nameof(OpeningDevices), Postfix: false)
        }
            .Append((Target: AccessTools.Method(typeof(GameItem), nameof(GameItem.DisplayManufacturer), new[] { typeof(RichTextBuilder), typeof(string) }),
                Callback: nameof(Manufacturer), Postfix: false))
            .Append((Target: AccessTools.Method(typeof(GameItem), nameof(GameItem.MayStack), new[] { typeof(GameItem) }),
                Callback: nameof(PreventToolStack), Postfix: false))
            .Concat(new[]
            {
                (Target: AccessTools.Method(typeof(ItemContextHandler), "EqualizeButtonWidths", Type.EmptyTypes),
                    Callback: nameof(MenuWidths), Postfix: true),
                (Target: AccessTools.Method(typeof(ItemContextHandler), "TriggerButton", new[] { typeof(RichTextElement) }),
                    Callback: nameof(MenuTrigger), Postfix: false)
            })
            .ToArray();
        hooks = hooks.Concat(new[]
        {
            (Target: AccessTools.Method(typeof(GameItem), nameof(GameItem.MayHaveValidInventorySlot), new[] { typeof(GameItem) }), Callback: nameof(DeviceInsertPreview), Postfix: false),
            (Target: AccessTools.Method(typeof(GameItem), nameof(GameItem.MayTarget), new[] { typeof(GameItem) }), Callback: nameof(DeviceCanTarget), Postfix: false),
            (Target: AccessTools.Method(typeof(GameItem), nameof(GameItem.CanTarget), new[] { typeof(GameItem) }), Callback: nameof(DeviceCanTarget), Postfix: false),
            (Target: AccessTools.Method(typeof(GameItem), nameof(GameItem.Target), new[] { typeof(GameItem) }), Callback: nameof(DeviceTarget), Postfix: false)
        }).ToArray();
        hooks = hooks.Concat(new[]
        {
            (Target: AccessTools.Method(typeof(PowerHelper), nameof(PowerHelper.GetAllPowerSourceFourniture), new[] { typeof(bool) }), Callback: nameof(AfterPowerFurniture), Postfix: true),
            (Target: AccessTools.Method(typeof(PowerHelper), nameof(PowerHelper.ChargeAllRechargable), new[] { typeof(System.Collections.Generic.List<GameItem>) }), Callback: nameof(BeforeChargeAll), Postfix: false)
        }).Concat(new[] { new[] { typeof(GameItem) }, new[] { typeof(GameItem), typeof(int), typeof(bool) } }.SelectMany(types => new[]
        {
            (Target: AccessTools.Method(typeof(PowerHelper), nameof(PowerHelper.ChargeBattery), types), Callback: nameof(BeforePowerCharge), Postfix: false),
            (Target: AccessTools.Method(typeof(PowerHelper), nameof(PowerHelper.ChargeBattery), types), Callback: nameof(AfterPowerCharge), Postfix: true)
        })).ToArray();
        hooks = hooks.Append((Target: AccessTools.Method(typeof(SpriteHelper), nameof(SpriteHelper.UpdateWaterContainerSprite), new[] { typeof(GameItem) }),
            Callback: nameof(WaterSprite), Postfix: false)).ToArray();
        hooks = hooks.Concat(new[]
        {
            (Target: AccessTools.Method(typeof(AquacultureHelper), nameof(AquacultureHelper.GeneratePower), new[] { typeof(GameItem), typeof(GameInventory) }),
                Callback: nameof(BeforeAquaculturePower), Postfix: false),
            (Target: AccessTools.Method(typeof(AquacultureHelper), nameof(AquacultureHelper.GeneratePower), new[] { typeof(GameItem), typeof(GameInventory) }),
                Callback: nameof(AfterAquaculturePower), Postfix: true)
        }).ToArray();
        // GameInventory.UncheckedAccept is abstract in the native game even
        // though its generated interop wrapper looks concrete. Hook overrides only.
        hooks = hooks.Concat(new[] {
            AccessTools.Method(typeof(GameGridInventory), nameof(GameGridInventory.UncheckedAccept), new[] { typeof(GameItem) }),
            AccessTools.Method(typeof(GameSlotInventory), nameof(GameSlotInventory.UncheckedAccept), new[] { typeof(GameItem) }),
            AccessTools.Method(typeof(GameGridScrollableInventory), nameof(GameGridScrollableInventory.UncheckedAccept), new[] { typeof(GameItem) }) }
            .Select(target => (Target: target,
                Callback: nameof(AfterInventoryAccept), Postfix: true))).ToArray();
        hooks = hooks.Concat(new[] {
            AccessTools.Method(typeof(GameGridInventory), nameof(GameGridInventory.MayHaveValidInventorySlot), new[] { typeof(GameItem) }),
            AccessTools.Method(typeof(GameSlotInventory), nameof(GameSlotInventory.MayHaveValidInventorySlot), new[] { typeof(GameItem) }),
            AccessTools.Method(typeof(GameGridScrollableInventory), nameof(GameGridScrollableInventory.MayHaveValidInventorySlot), new[] { typeof(GameItem) }) }
            .Select(target => (Target: target,
                Callback: nameof(PowerInventoryAdmission), Postfix: true))).ToArray();
        foreach (var group in hooks.GroupBy(h => Feature(h.Callback)))
        {
            // Preserve the furnace observer's public ordering identity.
            var harmony = new HarmonyLib.Harmony(Content.Owner + ".native" + (group.Key == "furnace" ? "" : "." + group.Key));
            try
            {
                foreach (var h in group)
                    if (!ValidateTarget(h.Target, out var reason)) throw new InvalidOperationException(reason);
                foreach (var h in group)
                {
                    var callback = new HarmonyMethod(typeof(NativeHooks), h.Callback);
                    if (h.Callback == nameof(AfterBeginDay)) callback.priority = Priority.Last;
                    if (h.Callback == nameof(AfterPowerCharge)) callback.priority = Priority.Last;
                    if (h.Callback == nameof(PowerInventoryAdmission)) callback.priority = Priority.Last;
                    if (h.Callback is nameof(DeviceInsertPreview) or nameof(DeviceCanTarget) or nameof(DeviceTarget))
                    {
                        callback.priority = int.MaxValue;
                        callback.before = ["ContainerUpgrade"];
                    }
                    harmony.Patch(h.Target!, prefix: h.Postfix ? null : callback,
                        postfix: h.Postfix ? callback : null,
                        finalizer: null);
                }
                Harmonies.Add(harmony);
                if (group.Key == "window") UiAvailable = true;
            }
            catch (Exception ex)
            {
                try { harmony.UnpatchSelf(); }
                catch (Exception cleanup) { Log?.Invoke("物流钩子清理失败：" + cleanup.Message); }
                Log?.Invoke("物流适配停用：" + group.Key + "; " + ex.Message);
            }
        }
        WaterTransferPatch.Start();
    }

    private static void PatchWaterTransfer(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(WaterHelper), nameof(WaterHelper.TransferLiquid), [typeof(GameItem), typeof(GameItem)]);
        if (!ValidateTarget(target, out var reason)) throw new InvalidOperationException(reason);
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(NativeHooks), nameof(AfterWaterTransfer)));
    }

    private static string Feature(string callback) => callback switch
    {
        nameof(ItemTooltip) or nameof(Manufacturer) => "tooltip",
        nameof(AfterBeginDay) => "begin_day",
        nameof(OpeningDevices) => "opening_terminal",
        nameof(PreventToolStack) => "tool_stack",
        nameof(MenuWidths) or nameof(MenuTrigger) or nameof(GatherMenuWidths) => "menus",
        nameof(DeviceInsertPreview) or nameof(DeviceCanTarget) or nameof(DeviceTarget) => "devices",
        nameof(StorageKitTabs) or nameof(StorageTabsChanged) => "storage_tabs",
        nameof(AfterPowerFurniture) or nameof(BeforeChargeAll) or nameof(BeforePowerCharge) or nameof(AfterPowerCharge) => "charging",
        nameof(WaterSprite) => "water_sprite",
        nameof(BatterySprite) => "battery_sprite",
        nameof(BeforeAquaculturePower) or nameof(AfterAquaculturePower) => "aquaculture_power",
        nameof(AfterInventoryAccept) => "inventory_observation",
        nameof(PowerInventoryAdmission) => "power_inventory_admission",
        _ => "window"
    };
    private static void BeforeAquaculturePower(GameItem __0, GameInventory __1,
        out AutomatedLogisticsRuntime.AquaculturePowerObservation? __state)
    {
        __state = null;
        try { __state = Runtime?.BeforeAquaculturePower(__0, __1); }
        catch (Exception ex) { Log?.Invoke("养殖发电观察失败：" + ex.Message); }
    }
    private static void AfterAquaculturePower(AutomatedLogisticsRuntime.AquaculturePowerObservation? __state, bool __runOriginal)
    {
        if (!__runOriginal) return;
        try { Runtime?.AfterAquaculturePower(__state); }
        catch (Exception ex) { Log?.Invoke("养殖发电入网失败：" + ex.Message); }
    }
    private static bool ValidateTarget(System.Reflection.MethodBase? target, out string reason)
    { reason = target == null ? "Required official game method missing" : ""; return target != null && !target.IsAbstract; }
    private static void AfterBeginDay(PlayerStore __instance, bool __runOriginal)
    {
        if (!__runOriginal || __instance == null || __instance.Handle() != PlayerStore.Instance?.Handle()) return;
        try { Runtime?.ObserveMorning(StoreStation.GetDayCounter()); }
        catch (Exception ex) { Log?.Invoke("早间资源统计：" + ex.Message); }
    }
    private static void AfterInventoryAccept(GameItem __0, bool __result)
    {
        // Defer draining until the enclosing native transfer has completed.
        if (__result && __0 != null) Runtime?.QueueGridEnergy(__0);
        if (__result && __0 != null)
            try { Runtime?.ObserveDispenserFood(__0); }
            catch (Exception ex) { Log?.Invoke("喂食器粮食记录：" + ex.Message); }
    }
    private static bool Manufacturer(GameItem __instance, RichTextBuilder __0)
    {
        if (__instance.identifier is not (Content.Terminal or Content.Feeder or Content.Ejector) &&
            !Content.IsDevice(__instance.identifier)) return true;
        __0.Add(" ");
        __0.AddLine(PsModI18n.I18n.Get("nicokobo_workshop"), color: RenderHandler.ColorPalette.Green, underline: true);
        return false;
    }
    private static void ItemTooltip(GameItemElement __instance, RichTextBuilder __result)
    {
        if (__instance.identifier != Content.PowerDevice || Runtime?.Ready != true) return;
        long total = Runtime.PowerTotal(), capacity = Runtime.PowerCapacity();
        __result.NextLine().Add(PsModI18n.I18n.Get("stored_grid_energy"), color: RenderHandler.ColorPalette.Cyan);
        __result.NextLine().AddProgressBar((float)Math.Min(total, capacity) / capacity, 4, color: RenderHandler.ColorPalette.Cyan, end: " ");
        __result.Add($"{total} / {capacity} kJ", color: RenderHandler.ColorPalette.Cyan);
    }
    private static void BatterySprite(GameItem __0) => ItemArtwork.ApplyDisposableBattery(__0);
    private static void AfterWaterTransfer(GameItem __0, GameItem __1)
    {
        Runtime?.RefreshDeviceWater(__0); Runtime?.RefreshDeviceWater(__1);
    }
    private static bool StorageKitTabs(GameItem __0, ref int __result)
    {
        // Native CanExpand, Expand, RestoreTabs and tooltips share this whitelist.
        if (__0?.identifier != Content.ResourceDevice) return true;
        __result = 1;
        return false;
    }
    private static void StorageTabsChanged(GameItem __0)
    {
        if (__0.identifier == Content.ResourceDevice) Runtime?.RefreshResourceTabs(__0);
    }
    private static bool WaterSprite(GameItem __0)
    {
        if (__0 == null || __0.identifier != Content.WaterDevice) return true;
        ItemArtwork.ApplyWater(__0);
        return false;
    }
    private static void BeforePowerCharge(GameItem __0, out AutomatedLogisticsRuntime.ChargeObservation? __state)
    {
        __state = null;
        try { __state = Runtime?.BeforeNativeCharge(__0); }
        catch (Exception ex) { Log?.Invoke("产电统计开始：" + ex.Message); }
    }
    private static void AfterPowerCharge(AutomatedLogisticsRuntime.ChargeObservation? __state)
    { try { Runtime?.AfterNativeCharge(__state); } catch (Exception ex) { Log?.Invoke("产电统计：" + ex.Message); } }
    private static void AfterPowerFurniture(System.Collections.Generic.List<GameItem> __result)
    {
        if (Runtime == null || __result == null) return;
        var members = Runtime.GridMembers();
        for (int index = __result.Count - 1; index >= 0; index--)
            if (__result[index] != null && members.Contains(__result[index].Handle())) __result.RemoveAt(index);
    }
    private static void BeforeChargeAll(ref System.Collections.Generic.List<GameItem> __0)
    {
        if (Runtime == null || __0 == null) return;
        var copy = new System.Collections.Generic.List<GameItem>(); var seen = new HashSet<IntPtr>();
        foreach (var item in __0.ToArray()) { copy.Add(item); if (item != null) seen.Add(item.Handle()); }
        foreach (var battery in Runtime.PowerBatteries()) if (seen.Add(battery.Handle())) copy.Add(battery);
        __0 = copy;
    }
    private static void OpeningDevices(PlayerStore __instance)
    {
        try { Runtime?.GrantOpeningPowerGrid(__instance); }
        catch (Exception ex) { Log?.Invoke("开局赠送虚空电网失败：" + ex.Message); }
        try { Runtime?.GrantOpeningTerminal(__instance); }
        catch (Exception ex) { Log?.Invoke("开局赠送终端失败：" + ex.Message); }
    }
    private static bool UpgradeHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
    private static void PowerInventoryAdmission(GameInventory __instance, GameItem __0, ref bool __result)
    {
        if (!__result) return;
        try { __result = NetworkStorage.AllowsPowerInventory(__0, __instance); }
        catch (Exception ex) { __result = false; Log?.Invoke("电网库存准入：" + ex.Message); }
    }
    private static bool DeviceInsertPreview(GameItem __instance, GameItem __0, ref bool __result)
    {
        // Native drag chooses insertion before MayTarget/CanTarget. Expansion
        // kits and Alt upgrades must reach their action even with free slots.
        if (!AutomatedLogisticsRuntime.PreferDeviceUpgrade(__0, __instance, UpgradeHeld)) return true;
        __result = false;
        return false;
    }
    private static bool DeviceCanTarget(GameItem __instance, GameItem __0, ref bool __result)
    {
        if (!InterceptDeviceTarget(__instance, __0)) return true;
        __result = false;
        try { __result = Runtime!.CanDeviceTarget(__instance, __0, UpgradeHeld); }
        catch (Exception ex) { Log?.Invoke("实体拖放预检：" + ex.Message); }
        return false;
    }
    private static bool DeviceTarget(GameItem __instance, GameItem __0)
    {
        if (!InterceptDeviceTarget(__instance, __0)) return true;
        try { Runtime!.DeviceTarget(__instance, __0, UpgradeHeld); }
        catch (Exception ex) { Log?.Invoke("实体拖放停止：" + ex); }
        return false;
    }
    private static bool InterceptDeviceTarget(GameItem source, GameItem target) =>
        Runtime?.HandlesDeviceTarget(source, target, UpgradeHeld) == true;
    private static bool Tool(GameItem item) => item.identifier == Content.Terminal || Content.IsDevice(item.identifier);
    private static bool SettingsModule(GameItem item) => item.identifier is Content.Feeder or Content.Ejector;
    private static GameItem? InstalledMachine(GameItem module)
    {
        if (!NativeInventoryApi.IsPlayerOwned(module) || module.parentInventory is not { } inventory) return null;
        var machine = inventory.GetParentItem();
        return machine != null && MachineAccess.Eligible(machine) &&
            NativeModuleApi.GetInventory(machine)?.Handle() == inventory.Handle() &&
            NativeModuleApi.FindInstalled(machine, module.identifier).Any(item => item.Handle() == module.Handle()) ? machine : null;
    }
    private static bool PreventToolStack(GameItem __instance, GameItem __0, ref bool __result)
    {
        if (!Tool(__instance) && (__0 == null || !Tool(__0))) return true;
        __result = false; return false;
    }
    private static bool CanActivate(GameItem __instance, ref bool __result)
    {
        if (SettingsModule(__instance))
        { __result = InstalledMachine(__instance) != null; return false; }
        if (__instance.identifier != Content.Terminal) return true;
        __result = NativeInventoryApi.IsPlayerOwned(__instance) && __instance.parentInventory != null;
        return false;
    }
    private static bool Activate(GameItem __instance)
    {
        if (SettingsModule(__instance))
        {
            if (InstalledMachine(__instance) is { } machine) OpenMachine?.Invoke(__instance, machine);
            return false;
        }
        if (__instance.identifier != Content.Terminal) return true;
        if (NativeInventoryApi.IsPlayerOwned(__instance) && __instance.parentInventory != null) Open?.Invoke(__instance);
        return false;
    }
    internal static void Opened()
    {
        Capture.Open(Time.frameCount, MouseHeld);
        NativeAccess.Reset(ItemMouseDragHandler.current);
        InputActionManager.current?.Reset();
        CustomEventHandler.ClearDoubleClickTarget();
    }
    private static bool InputPrefix(InputActionManager __instance)
    {
        if (PointerInside?.Invoke() == true && (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)))
            NativeAccess.Reset(ItemMouseDragHandler.current);
        if (!Blocks && TextEditing?.Invoke() != true) return true;
        __instance.Reset(); return false;
    }
    private static bool DoubleClickPrefix(CustomEventHandler __instance)
    {
        if (!Blocks) return true;
        CustomEventHandler.ClearDoubleClickTarget(); return false;
    }
    private static bool RaycastPrefix(StoreUIManager __instance)
    { if (!Blocks) return true; __instance.hoveringStoreUI = true; return false; }
    private static void StorePostfix(StoreUIManager __instance)
    { if (Blocks) __instance.hoveringStoreUI = true; }
    private static bool Escape(ref bool __result)
    {
        if (Visible?.Invoke() != true) return true;
        if (DismissPopup?.Invoke() != true) Close?.Invoke();
        __result = true; return false;
    }
    private static void ClosePostfix() => Close?.Invoke();
    // OnEventPress calls this native method before validating and showing the
    // menu. UpdateConditions is inlined there and is not a reliable hook point.
    private static void MenuWidths(ItemContextHandler __instance)
    { try { NativeRenameMenu.Refresh(__instance); } catch (Exception ex) { Log?.Invoke("右键重命名菜单：" + ex); } }
    private static bool MenuTrigger(ItemContextHandler __instance, RichTextElement __0)
    { return !NativeStorageGatherMenu.Trigger(__instance, __0) && !NativeRenameMenu.Trigger(__instance, __0); }
    private static void GatherMenuWidths(ItemContextHandler __instance)
    { try { NativeStorageGatherMenu.Refresh(__instance); } catch (Exception ex) { Log?.Invoke("虚空容器归集菜单：" + ex); } }
    internal static void Uninstall()
    {
        UiAvailable = false;
        WaterTransferPatch.Stop();
        foreach (var harmony in Harmonies)
            try { harmony.UnpatchSelf(); }
            catch (Exception ex) { Log?.Invoke("物流钩子清理失败：" + ex.Message); }
        Harmonies.Clear(); NativeStorageGatherMenu.Clear(); NativeRenameMenu.Clear(); Capture.Reset();
    }
}
