using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private object TheftRun() => Reflect.Get(ModLoader.Mods.Single(m => m.Manifest.ID == "nicokobo.definitely_stolen").Instance, "Run")!;
    private object? TheftState() => Reflect.Get(TheftRun(), "State");
    private object TheftNight() => Reflect.Get(TheftState()!, "Night") ?? throw new InvalidOperationException("Theft night is unavailable");
    private IEnumerable<object> Npcs() => Reflect.Each(Reflect.Get(TheftNight(), "Npcs"));
    internal IEnumerable<object?> Theft()
    {
        R.phase = "theft-normal-career-opening";
        object run = TheftRun(), state = TheftState()!;
        var opening = Reflect.Each(Reflect.Get(state, "OpeningItems")).ToArray();
        int cashDelta = Convert.ToInt32(Reflect.Get(state, "OpeningCashDelta"));
        long openingValue = opening.Where(i => Reflect.Str(i, "Id") != "black_lamp").Sum(i => Convert.ToInt64(Reflect.Get(i, "Value")));
        R.Check("theft.native-career-opening", new { qualified = true, source = "career", experience = 75, granted = true, variableGoodsPlusCash = 500 },
            new { qualified = Reflect.Get(state, "Qualified"), source = Reflect.Str(state, "QualificationSource"), experience = Reflect.Int(state, "Experience"),
                granted = Reflect.Get(state, "OpeningGranted"), cashDelta, openingValue, opening = Reflect.Json(opening) },
            (bool)Reflect.Get(state, "Qualified")! && Reflect.Str(state, "QualificationSource") == "career" && Reflect.Int(state, "Experience") == 75 &&
                (bool)Reflect.Get(state, "OpeningGranted")! && cashDelta + openingValue == 500,
            "Registered MODDED starting scenario -> ordinary OnNewGameStarted -> native InitialSave hooks");
        var lower = Store.storeReputations.Single(x => x.factionId == "FACTION_LOWER_LEVEL");
        R.Check("theft.opening-lower-reputation", -15d, lower.GetReputationExact(), lower.GetReputationExact() == -15d);
        bool generated = (bool)Reflect.Call(run, "EnsureNight")!;
        var targets = Npcs().ToArray();
        int[] candidateIds = targets.SelectMany(n => Reflect.Each(Reflect.Get(n, "Items"))).Select(i => Reflect.Int(i, "UniqueId")).ToArray();
        var live = AllItems().Select(i => i.uniqueId).ToHashSet();
        R.Check("theft.real-generated-targets", new { targets = 9, perTarget = 5, uniqueCandidateIds = 45, noPlayerCollision = true },
            new { generated, targets = targets.Length, sizes = targets.Select(n => Reflect.Each(Reflect.Get(n, "Items")).Count()).ToArray(), candidateIds },
            generated && targets.Length == 9 && targets.All(n => Reflect.Each(Reflect.Get(n, "Items")).Count() == 5) && candidateIds.Length == 45 && candidateIds.Distinct().Count() == 45 && candidateIds.All(id => id > 0 && !live.Contains(id)));
        // Use the real close-shop flow; it allocates the one native outing and
        // fires both shutter hooks. No action counter is fabricated.
        StoreShutterButton.Instance.EndAndClose();
        while (Store.storeState != PlayerStore.StoreState.CLOSED) yield return null;
        var location = ModHelper.GetAfterhourLocation("nicokobo.definitely_stolen.lower_night_market") ?? throw new InvalidOperationException("Registered lower-night-market location missing");
        MapUIManager.Instance.Travel(location);
        while (MapUIManager.Instance.Current?.Id != location.Id || !(bool)Reflect.Get(run, "Browsing")!) yield return null;
        var backpack = Spawn("backpack_medium");
        if (EmporiumEntry.Instance.afterhourPocketSlotInvBackpack.childItem == null) Place(EmporiumEntry.Instance.afterhourPocketSlotInvBackpack, backpack, "theft-real-backpack");
        else Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(backpack);
        var smoke = Spawn("nicokobo.definitely_stolen.escape_smoke");
        Place(EmporiumEntry.Instance.afterhourPocketSlotInvRight, smoke, "theft-carry-smoke");
        int smokeUid = smoke.uniqueId;
        object target = Npcs().First(n => Reflect.Int(n, "Region") == 0);
        string key = Reflect.Str(target, "Key");
        int xpBeforePractice = Reflect.Int(TheftState()!, "Experience"), visitsBeforePractice = Store.visitLeftTonight;
        int[] idsBeforePractice = AllItems().Select(i => i.uniqueId).OrderBy(i => i).ToArray();
        var npcBeforePractice = Reflect.Json(Reflect.Get(target, "Items"));
        object? practice = Reflect.Call(run, "BeginPractice", key);
        object? duplicatePractice = Reflect.Call(run, "BeginPractice", key);
        R.Check("theft.manual-practice-one-time-no-custody", new { availableOnce = true, xp = xpBeforePractice, visits = visitsBeforePractice, uids = idsBeforePractice, candidates = npcBeforePractice },
            new { practice = practice != null, duplicate = duplicatePractice != null, xp = Reflect.Int(TheftState()!, "Experience"), visits = Store.visitLeftTonight,
                uids = AllItems().Select(i => i.uniqueId).OrderBy(i => i).ToArray() },
            practice != null && duplicatePractice == null && Reflect.Int(TheftState()!, "Experience") == xpBeforePractice && Store.visitLeftTonight == visitsBeforePractice &&
                idsBeforePractice.SequenceEqual(AllItems().Select(i => i.uniqueId).OrderBy(i => i)) && JToken.DeepEquals(npcBeforePractice, Reflect.Json(Reflect.Get(Npcs().First(n => Reflect.Str(n, "Key") == key), "Items"))));
        R.phase = "theft-real-manual-transaction";
        bool begun = (bool)Reflect.Call(run, "BeginTheft", key)!;
        int candidatesBefore = Reflect.Each(Reflect.Get(Npcs().First(n => Reflect.Str(n, "Key") == key), "Items")).Count();
        Reflect.Call(run, "StopTheft", .5d);
        object? manualReceipt = Reflect.Get(TheftState()!, "LastAction");
        int stolenUid = manualReceipt == null ? 0 : Reflect.Int(manualReceipt, "ItemUniqueId");
        int xpAfterManual = Reflect.Int(TheftState()!, "Experience");
        R.Check("theft.manual-actual-loot-custody", new { outcome = "Success", candidateDebit = 1, actualOwnedUid = true, visits = visitsBeforePractice - 1, xpGain = "5 common or 10 rare" },
            new { begun, receipt = Reflect.Json(manualReceipt), physical = AllItems().Where(i => i.uniqueId == stolenUid).Select(ItemState).ToArray(), visits = Store.visitLeftTonight, xp = xpAfterManual },
            begun && manualReceipt != null && Reflect.Str(manualReceipt, "Outcome") == "Success" && !(bool)Reflect.Get(manualReceipt, "Debug")! &&
                candidatesBefore - Reflect.Each(Reflect.Get(Npcs().First(n => Reflect.Str(n, "Key") == key), "Items")).Count() == 1 &&
                AllItems().Count(i => i.uniqueId == stolenUid && GeneralHelper.IsItemOwned(i)) == 1 && Store.visitLeftTonight == visitsBeforePractice - 1 &&
                (xpAfterManual - xpBeforePractice is 5 or 10));
        R.phase = "theft-normal-automatic-animation";
        var modeType = Reflect.Type("DefinitelyStolen.Domain.TheftMode");
        Reflect.Call(run, "SelectMode", Enum.Parse(modeType, "Probability"));
        string autoTarget = Reflect.Str(Npcs().First(n => Reflect.Int(n, "Region") == 0 && Reflect.Str(n, "Key") != key), "Key");
        int attemptsBefore = Reflect.Int(TheftNight(), "Attempts");
        bool automaticBegun = (bool)Reflect.Call(run, "BeginTheft", autoTarget)!;
        float automaticStart = Time.realtimeSinceStartup;
        while (Reflect.Get(TheftNight(), "Timing") != null)
        {
            if (Time.realtimeSinceStartup - automaticStart > 10f) throw new TimeoutException("Normal TheftPanel.Tick did not finish the actual automatic attempt");
            yield return null;
        }
        object? automaticReceipt = Reflect.Get(TheftState()!, "LastAction");
        R.Check("theft.actual-automatic-attempt", new { oneAttempt = true, mode = "Probability", debug = false, newOutingDebit = 0 },
            new { automaticBegun, receipt = Reflect.Json(automaticReceipt), attempts = Reflect.Int(TheftNight(), "Attempts"), visits = Store.visitLeftTonight },
            automaticBegun && Reflect.Int(TheftNight(), "Attempts") == attemptsBefore + 1 && automaticReceipt != null &&
                Reflect.Str(automaticReceipt, "Mode") == "Probability" && !(bool)Reflect.Get(automaticReceipt, "Debug")! && Store.visitLeftTonight == visitsBeforePractice - 1,
            "Native registered location panel remains visible; ordinary production TheftPanel.Tick resolves animation and transaction");
        if (!(bool)Reflect.Get(TheftNight(), "Escaped")! && !(bool)Reflect.Get(TheftNight(), "Ended")!)
        {
            Reflect.Call(run, "SelectMode", Enum.Parse(modeType, "Timing"));
            object caughtTarget = Npcs().First(n => Reflect.Int(n, "Region") == 0 && Reflect.Each(Reflect.Get(n, "Items")).Any());
            var repsBefore = Store.storeReputations.ToDictionary(p => p.factionId, p => p.GetReputationExact());
            int[] lootBefore = Reflect.Each(Reflect.Get(TheftNight(), "Loot")).Select(i => Reflect.Int(i, "UniqueId")).OrderBy(i => i).ToArray();
            bool caughtBegun = (bool)Reflect.Call(run, "BeginTheft", Reflect.Str(caughtTarget, "Key"))!;
            Reflect.Call(run, "StopTheft", 0d);
            object? caught = Reflect.Get(TheftState()!, "LastAction");
            string faction = caught == null ? "" : Reflect.Str(caught, "Faction");
            string[] affected = new[] { "FACTION_SECURITY", faction }.Distinct().Where(repsBefore.ContainsKey).ToArray();
            int[] lootAfter = Reflect.Each(Reflect.Get(TheftNight(), "Loot")).Select(i => Reflect.Int(i, "UniqueId")).OrderBy(i => i).ToArray();
            R.Check("theft.smoke-caught-real-carry-and-reputation", new { outcome = "Caught", escaped = true, consumedUid = smokeUid, retainedLoot = lootBefore, penaltyEach = 1 },
                new { caughtBegun, receipt = Reflect.Json(caught), escaped = Reflect.Get(TheftNight(), "Escaped"), loot = lootAfter,
                    reps = Store.storeReputations.Where(p => affected.Contains(p.factionId)).ToDictionary(p => p.factionId, p => p.GetReputationExact()) },
                caughtBegun && caught != null && Reflect.Str(caught, "Outcome") == "Caught" && Reflect.Int(caught, "EscapeItemUniqueId") == smokeUid &&
                    (bool)Reflect.Get(TheftNight(), "Escaped")! && AllItems().All(i => i.uniqueId != smokeUid) && lootBefore.SequenceEqual(lootAfter) &&
                    lootAfter.All(uid => AllItems().Any(i => i.uniqueId == uid)) && affected.All(id => Store.storeReputations.Single(p => p.factionId == id).GetReputationExact() == repsBefore[id] - 1));
        }
        else
        {
            R.Check("theft.smoke-automatic-caught", new { consumedUid = smokeUid, escaped = true, actualLootRetained = true }, Reflect.Json(automaticReceipt),
                automaticReceipt != null && Reflect.Int(automaticReceipt, "EscapeItemUniqueId") == smokeUid && (bool)Reflect.Get(TheftNight(), "Escaped")! && AllItems().All(i => i.uniqueId != smokeUid) && AllItems().Any(i => i.uniqueId == stolenUid));
            R.Skip("theft.manual-caught-reputation", "The legitimate automatic result consumed smoke and ended theft before a separate manual caught attempt; automatic receipt and carry were checked", false);
        }
        R.persistence["theftExpectedState"] = Reflect.Json(TheftState());
        R.persistence["theftCarry"] = Reflect.Json(Reflect.Call(Reflect.Type("DefinitelyStolen.Integration.NativeCarry"), "Snapshot"));
        R.Skip("theft.habitual-and-perk-only-start", "Current process tests the normal career start at 75 XP; Habitual 350 XP and noncareer Streetwise require separate new-game processes", false);
    }
}
