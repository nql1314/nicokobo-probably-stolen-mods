using HarmonyLib;
using MechcoreProtocol.SynthesisExpansion.Definitions;
using MechcoreProtocol.SynthesisExpansion.Domain;
using UnityEngine;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class SynthesisRobots
{
    [ThreadStatic] internal static GameItem? Scavenging;
    [ThreadStatic] internal static bool ReadingQuota;
    internal static bool IsRobot(GameItem item) => item.identifier == SynthesisDefinitions.ScavengingRobot.Id;
    internal static bool IsDrill(GameItem item) => item.identifier == SynthesisDefinitions.MiningDrill.Id;
    internal static void Reset() { Scavenging = null; ReadingQuota = false; }

    internal static GameItem Create()
    {
        // Native item construction already invokes appearance callbacks. Publish
        // the robot identity only after its battery, cargo and saved tags exist.
        var robot = ItemDirectory.CreateEmptyItem(string.Empty);
        var battery = new GameSlotInventory { identifier = SynthesisDefinitions.OwnerId + ".scavenging_robot.battery" };
        var cargo = new GameGridInventory(SynthesisNumbers.ScavengingRobot.DefaultWidth, SynthesisNumbers.ScavengingRobot.DefaultHeight)
        { identifier = SynthesisDefinitions.OwnerId + ".scavenging_robot.contents" };
        var toggle = new MachineryButton().SetSize(16, 16).SetFillWidth(false)
            .SetDescription(SynthesisText.Get("synthesis.collect_junk_green_on_red_off"));
        void RefreshToggle() => toggle.SetFaceSprites(RenderHandler.LoadFromAtlas("Window/elements_inner",
            robot.IsTag(ScavengingRobotRules.SkipJunkTag) ? "button_up_red" : "button_up_green"));
        toggle.SetCallback(() =>
        {
            robot.ModifyTag(ScavengingRobotRules.SkipJunkTag, tag => tag.SetEnabled(!robot.IsTag(ScavengingRobotRules.SkipJunkTag)));
            RefreshToggle();
        });
        // Battery/cargo order is part of the official save topology.
        robot.SetContentWindow(MachineHelper.CreateMachineryWindow(true, MachineHelper.PowerPanel(battery),
            MachineHelper.Panel(SynthesisText.Get("synthesis.storage"), cargo, "storage"),
            MachineHelper.Panel(null, MachineryStack.Vertical(new MachineryText(SynthesisText.Get("synthesis.junk")), toggle), "settings")));
        ContainerHelper.InitContainerItem(cargo, robot);
        MachineHelper.SetupBatterySlot(battery, robot);
        battery.mayInventoryAddItemFunc = (item, _) => item.unitCount == 1 && GeneralHelper.IsItemOwned(item) && item.IsTag("power_source_item");
        robot.SetGameItemType("MACHINE"); robot.SetGameItemType("STORAGE");
        robot.onLoaded += item => { RestoreStorage(item); RefreshToggle(); RobotAppearance.Apply(item); };
        SetStorageShape(robot, SynthesisNumbers.ScavengingRobot.DefaultWidth,
            new string('0', SynthesisNumbers.ScavengingRobot.DefaultWidth * SynthesisNumbers.ScavengingRobot.DefaultHeight));
        SetScannerChance(robot, SynthesisNumbers.ScavengingRobot.InitialScannerChance);
        robot.ModifyTag(ScavengingRobotRules.BrokenTag, tag => tag.SetEnabled(false));
        robot.ModifyTag(ScavengingRobotRules.LastNightTag, tag => tag.Enable().SetString(""));
        robot.ModifyTag(ScavengingRobotRules.SkipJunkTag, tag => tag.SetEnabled(false));
        robot.identifier = SynthesisDefinitions.ScavengingRobot.Id;
        RefreshToggle();
        return robot;
    }

    internal static GameGridInventory Cargo(GameItem robot) => MachineHelper.GetBodyStorage(robot, 1) as GameGridInventory
        ?? throw new InvalidOperationException("Robot cargo inventory missing");
    internal static GameSlotInventory Battery(GameItem robot) => MachineHelper.GetBodyStorage(robot, 0) as GameSlotInventory
        ?? throw new InvalidOperationException("Robot battery slot missing");

    internal static void SetScannerChance(GameItem item, int chance)
    {
        if (!ScavengingRobotRules.IsValidScannerChance(chance)) throw new ArgumentOutOfRangeException(nameof(chance));
        item.ModifyTag(ScavengingRobotRules.ScannerChanceTag, tag => tag.Enable().SetInt(chance));
        if (item.GetTagReadonly(ScavengingRobotRules.ScannerChanceTag).valueInt != chance)
            throw new InvalidOperationException("Robot scanner level readback failed");
    }

    internal static void SetStorageShape(GameItem robot, int width, string data)
    {
        ApplyStorageShape(robot, width, data);
        robot.ModifyTag(ScavengingRobotRules.StorageShapeTag, tag => tag.Enable().SetInt(width).SetString(data));
        var saved = robot.GetTagReadonly(ScavengingRobotRules.StorageShapeTag);
        if (saved.valueInt != width || saved.valueString != data) throw new InvalidOperationException("Robot storage state readback failed");
    }

    internal static void RestoreStorage(GameItem robot)
    {
        var shape = robot.GetTagReadonly(ScavengingRobotRules.StorageShapeTag);
        ApplyStorageShape(robot, shape.valueInt, shape.valueString);
    }

    private static void ApplyStorageShape(GameItem robot, int width, string? data)
    {
        if (data == null || !ScavengingRobotRules.IsValidStorageShape(width, data)) throw new InvalidOperationException("Invalid robot storage shape");
        var cargo = Cargo(robot);
        int height = data.Length / width;
        bool Matches() => cargo.inventoryShape.width == width && cargo.inventoryShape.height == height &&
            Enumerable.Range(0, data.Length).All(index => cargo.inventoryShape.GetLocal(index % width, index / width) == data[index] - '0');
        if (Matches()) return;
        if (cargo.childItems.Count != 0) throw new InvalidOperationException("Cannot reshape populated robot storage");
        cargo.SetShape(data, width);
        robot.contentWindow.Validate();
        if (!Matches()) throw new InvalidOperationException("Robot storage shape readback failed");
    }

    internal static void Night()
    {
        var store = SynthesisMod.Store;
        if (store == null) return;
        string night = ScavengingRobotRules.NightKey(store.saveSlotId, store.runID, StoreStation.GetDayCounter());
        if (night.Length == 0) return;
        var robots = store.FindAllItem().Where(item => IsRobot(item) && item.unitCount == 1 && GeneralHelper.IsItemOwned(item)).Distinct().ToArray();
        if (robots.Length == 0) return;
        int attempts;
        bool previous = ReadingQuota;
        try { ReadingQuota = true; attempts = ScavHelper.GetMaxScavAttempts(); }
        finally { ReadingQuota = previous; }
        if (attempts <= 0) return;
        foreach (var robot in robots)
        {
            try { Work(robot, night, attempts); }
            catch (Exception error) { SynthesisMod.Warn($"Robot night stopped; item={robot.uniqueId}: {error.Message}"); }
        }
    }

    internal static void Work(GameItem robot, string night, int attempts)
    {
        var slot = Battery(robot);
        var battery = slot.childItem;
        string previous = robot.GetTagReadonly(ScavengingRobotRules.LastNightTag).valueString ?? "";
        int? energy = battery == null ? null : SynthesisPower.Energy(battery);
        if (attempts <= 0 || !GeneralHelper.IsItemOwned(robot) || !ScavengingRobotRules.CanWork(
            robot.IsTag(ScavengingRobotRules.BrokenTag), previous, night, energy)) return;
        robot.ModifyTag(ScavengingRobotRules.LastNightTag, tag => tag.Enable().SetString(night));
        var debit = SynthesisPower.Debit(slot, SynthesisNumbers.ScavengingRobot.NightlyPowerCost);
        if (debit != PowerDebit.Committed)
        {
            if (debit == PowerDebit.Rejected)
                robot.ModifyTag(ScavengingRobotRules.LastNightTag, tag => tag.Enable().SetString(previous));
            else SynthesisMod.Warn("Robot power source is uncertain; this night is marked to prevent retries");
            return;
        }
        bool breaks = ScavengingRobotRules.BreaksDown(UnityEngine.Random.Range(0, 100));
        try { Gather(robot, Cargo(robot), attempts); }
        finally
        {
            if (breaks) robot.ModifyTag(ScavengingRobotRules.BrokenTag, tag => tag.Enable());
            RobotAppearance.Apply(robot);
        }
    }

    private static void Gather(GameItem robot, GameGridInventory cargo, int attempts)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            List<GameItem> loot;
            var previous = Scavenging;
            try { Scavenging = robot; loot = ScavHelper.GetRandomScavengedItem(); }
            finally { Scavenging = previous; }
            bool full = false;
            try
            {
                foreach (var product in loot)
                {
                    if (product == null) continue;
                    if (product.parentInventory != null) throw new InvalidOperationException("Scavenging returned an attached item");
                    if (product.identifier == "junk" && robot.IsTag(ScavengingRobotRules.SkipJunkTag)) continue;
                    GeneralHelper.SetItemOwned(product, true);
                    if (!Place(cargo, product)) full = true;
                }
            }
            finally
            {
                foreach (var product in loot)
                    if (product != null && product.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(product);
            }
            if (full) break;
        }
    }

    internal static bool Place(GameGridInventory cargo, GameItem product)
    {
        var admission = cargo.mayInventoryAddItemFunc;
        bool locked = cargo.overrideLockInsert;
        var stacking = product.mayStackItemFunc;
        cargo.mayInventoryAddItemFunc = null; cargo.overrideLockInsert = false; product.mayStackItemFunc = null;
        try
        {
            var slot = cargo.TryFindOneValidInventorySlot(product);
            if (slot == null || slot.targetItem != null || !slot.IsValid() || slot.numTransfer != product.unitCount) return false;
            slot.AcceptUnchecked();
            if (product.parentInventory != cargo) throw new InvalidOperationException("Robot loot placement readback failed");
            return true;
        }
        finally { cargo.mayInventoryAddItemFunc = admission; cargo.overrideLockInsert = locked; product.mayStackItemFunc = stacking; }
    }

    internal static GameItem? FindTool(string id)
    {
        var robot = Scavenging;
        if (robot == null) return null;
        if (id == "metal_scanner") return robot;
        return robot.FindAllChildItems().FirstOrDefault(item => item.identifier == id && GeneralHelper.IsItemOwned(item));
    }

    internal static GameItem? FindDrill()
    {
        var items = Scavenging != null ? Scavenging.FindAllChildItems() : EmporiumEntry.Instance?.GetAllAfterhourOwnedItems();
        return items?.Where(item => IsDrill(item) && GeneralHelper.IsItemOwned(item))
            .OrderByDescending(item => item.IsTag(MiningDrillRules.UpgradedTag)).FirstOrDefault();
    }
}

[HarmonyPatch(typeof(StartingPerk), nameof(StartingPerk.IsDegenerativeOsteoarthritis))]
internal static class RobotArthritisPatch
{
    private static void Postfix(ref bool __result) { if (SynthesisMod.Active && SynthesisRobots.ReadingQuota) __result = false; }
}

[HarmonyPatch(typeof(StartingPerk), nameof(StartingPerk.IsMaimed))]
internal static class RobotMaimedPatch
{
    private static void Postfix(ref bool __result) { if (SynthesisMod.Active && SynthesisRobots.ReadingQuota) __result = false; }
}

[HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.IsAfterhourHaveOwnedItems))]
internal static class RobotToolPresencePatch
{
    private static bool Prefix(string __0, ref bool __result)
    {
        if (!SynthesisMod.Active || SynthesisRobots.Scavenging == null) return true;
        __result = SynthesisRobots.FindTool(__0) != null; return false;
    }
}

[HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.GetAfterhourItemById))]
internal static class RobotToolLookupPatch
{
    private static bool Prefix(string __0, ref GameItem? __result)
    {
        if (!SynthesisMod.Active || SynthesisRobots.Scavenging == null) return true;
        __result = SynthesisRobots.FindTool(__0); return false;
    }
}

// Both the player's search and the parameterless robot wrapper reach this
// overload, so each actual search receives at most one drill roll.
[HarmonyPatch(typeof(ScavHelper), nameof(ScavHelper.GetRandomScavengedItem), new[] { typeof(GameItem) }, new[] { ArgumentType.Out })]
internal static class MiningDrillLootPatch
{
    private static void Postfix(List<GameItem> __result)
    {
        if (!SynthesisMod.Active || __result == null) return;
        var drill = SynthesisRobots.FindDrill();
        if (drill == null || !MiningDrillRules.FindsOre(UnityEngine.Random.Range(0, 100), drill.IsTag(MiningDrillRules.UpgradedTag))) return;
        GameItem? ore = null;
        try
        {
            string id = MiningDrillRules.SelectOre(UnityEngine.Random.Range(0, 100));
            ore = DirectoryMaster.Item(id, false);
            if (ore == null || ore.identifier != id || ore.unitCount != 1 || ore.parentInventory != null)
                throw new InvalidOperationException("Drill ore factory contract failed");
            __result.Add(ore);
        }
        catch (Exception error)
        {
            if (ore?.parentInventory == null) ore?.Destroy();
            SynthesisMod.Warn("Mining drill extra ore skipped: " + error.Message);
        }
    }
}
