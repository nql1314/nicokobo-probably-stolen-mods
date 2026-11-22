using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private IEnumerable<object?> GraylineFailureRecovery(GraylineObservation observation, List<GameItem> purchased)
    {
        R.phase = "grayline.failure-recovery";
        var failedItem = Reflect.Each(Reflect.Get(GraylineFlow, "_graylineCounter"))
            .Select(pair => (GameItem)Reflect.Get(Reflect.Get(pair, "Value")!, "Item")!)
            .First(item => !GeneralHelper.IsItemOwned(item) &&
                (item.identifier == GraylineMaterial + "quartz" || item.identifier == GraylineMaterial + "titanium_ore"));
        JObject before = GraylineLedger([failedItem], observation);
        Exception? failure = null;
        using (var fault = new GraylinePurchaseFault(O.RunId, failedItem))
        {
            try { Store.BuyItem(failedItem); }
            catch (Exception error) { failure = error; }
            R.environment["graylineExpectedPurchaseFault"] = new
            {
                input = "Throw once before the selected native OnItemBought callback, after the real Grayline preparation",
                uid = failedItem.uniqueId, fault.Hits, exception = failure?.ToString()
            };
            GraylineRequire("grayline.native-failure-pauses-this-visit", "One injected native exception; pending intent, unchanged cash/items and earlier receipts",
                GraylineLedger([failedItem], observation),
                fault.Hits == 1 && failure?.ToString().Contains(fault.Marker) == true &&
                (bool)Reflect.Get(GraylineFlow, "_graylinePurchaseFailed")! &&
                !(bool)Reflect.Get(GraylineFlow, "_graylineBusy")! &&
                GraylineSupplyState()["Pending"]?.Type == JTokenType.Object &&
                Store.playerCash == (int)before["cash"]! && !GeneralHelper.IsItemOwned(failedItem) &&
                JToken.DeepEquals(before["supply"]!["Receipts"], GraylineSupplyState()["Receipts"]));
        }
        GraylineReject("grayline.failed-visit-does-not-replay", [failedItem], () => Store.BuyItem(failedItem), observation);
        GraylineSaveWithModBusyFlags();

        string failedAppointment = (string)GraylineSupplyState()["AppointmentId"]!;
        foreach (object? wait in GraylineDismissVisit("failed")) yield return wait;
        GraylineRequire("grayline.failed-visit-retired", "Closed visit, no pending purchase, earlier receipts preserved",
            GraylineSupplyState(), GraylineSupplyState()["Pending"]?.Type == JTokenType.Null &&
            JToken.DeepEquals(before["supply"]!["Receipts"], GraylineSupplyState()["Receipts"]) &&
            !(bool)Reflect.Get(GraylineFlow, "_graylinePurchaseFailed")!);

        foreach (object? wait in GraylineNight(1, 0, observation)) yield return wait;
        int nextDayEventStart = observation.Events.Count;
        foreach (object? wait in GraylineOpen(clearAfterOpen: false)) yield return wait;
        var queue = Store.storeClientManager.clientStack;
        var unrelated = queue.Where(client => client.identifier != GraylineActor).ToArray();
        string? current = Store.currentClientInstance?.GetClientBlueprint()?.identifier;
        R.environment["graylineNextVisitQueueFixture"] = new
        {
            purpose = "Remove unrelated native customers from the input queue, preserving any real Grayline request",
            removedQueue = unrelated.Select(client => client.identifier).ToArray(), removedCurrent = current != GraylineActor ? current : null
        };
        foreach (var client in unrelated) queue.Remove(client);
        if (current != null && current != GraylineActor) Store.DismissCurrentClient(false);
        GraylineRequire("grayline.fixture.next-visit-without-natural-customers", "Only the current Grayline supplier may remain in the input queue", GraylineUiState(),
            queue.All(client => client.identifier == GraylineActor) &&
            (Store.currentClientInstance == null || Store.currentClientInstance.GetClientBlueprint()?.identifier == GraylineActor));
        foreach (object? wait in GraylineServeUntil(() =>
            Store.currentClientInstance?.GetClientBlueprint()?.identifier == GraylineActor && Store.isClientArrived &&
            (string?)GraylineSupplyState()["VisitState"] == "arrived", GraylineActor, "grayline.next-tuesday-arrival")) yield return wait;
        GraylineRequire("grayline.next-visit-has-fresh-stock", "A different D2 Tuesday appointment with no pending purchase or receipt",
            GraylineSupplyState(), (string?)GraylineSupplyState()["AppointmentId"] != failedAppointment &&
            (int?)GraylineSupplyState()["DueDay"] == 2 &&
            GraylineSupplyState()["Pending"]?.Type == JTokenType.Null &&
            ((JArray)GraylineSupplyState()["Receipts"]!).Count == 0);
        foreach (object? wait in GraylineFinishLine("grayline.next-supplier-introduction")) yield return wait;
        GraylineStowOwnedCounter();
        var offer = Reflect.Each(Reflect.Get(GraylineFlow, "_graylineCounter"))
            .Select(pair => Reflect.Get(pair, "Value")!)
            .First(value => ((GameItem)Reflect.Get(value, "Item")!).identifier == GraylineMaterial + "quartz");
        var item = (GameItem)Reflect.Get(offer, "Item")!;
        observation.PurchaseUids.Add(item.uniqueId);
        GraylineTransfer(item, EmporiumEntry.Instance.backInvinvElement, "next-visit-to-weighing-counter");
        GraylinePurchase("grayline.next-visit-purchase", [item],
            new Dictionary<int, string> { [item.uniqueId] = Reflect.Str(offer, "Operation") },
            () => Store.BuyItemFromTable(), observation);
        GraylineTransfer(item, EmporiumEntry.Instance.invElement, "next-visit-bought-to-storage");
        Retain(item);
        purchased.Add(item);
        foreach (object? wait in GraylineDismissVisit("recovered")) yield return wait;
        foreach (object? wait in GraylineNight(2, nextDayEventStart, observation)) yield return wait;
        foreach (object? wait in GraylineWait("grayline.following-week-scheduled", () =>
            (string?)GraylineSupplyState()["VisitState"] == "waiting" &&
            (int?)GraylineSupplyState()["DueDay"] == 9)) yield return wait;
        GraylineRequire("grayline.recovery-completed", "Two actual nights and four completed native purchases",
            observation.Evidence, observation.Nights.Count == 2 && observation.Buys.Count == 4 &&
            observation.ObserverErrors.Count == 0 &&
            observation.PurchaseUids.SetEquals(observation.Buys.Select(row => (int)row["uid"]!)));
    }

    private IEnumerable<object?> GraylineDismissVisit(string name)
    {
        foreach (object? wait in GraylineFinishLine("grayline." + name + ".remaining-dialogue")) yield return wait;
        GraylineRequire("grayline." + name + ".can-dismiss", "Native supplier dismissability", GraylineUiState(), Store.IsClientDimissable());
        GraylineRingBell();
        foreach (object? wait in GraylineWait("grayline." + name + ".closed", () =>
            Store.currentClientInstance == null && (string?)GraylineSupplyState()["VisitState"] == "closed")) yield return wait;
    }

    private void GraylineSaveWithModBusyFlags()
    {
        R.phase = "grayline.busy-save";
        var flags = new[]
        {
            (Target: (object?)GraylineFlow, Field: GraylineFlow.GetType().GetField("_busy", Reflect.All)!),
            (Target: (object?)GraylineFlow, Field: GraylineFlow.GetType().GetField("_graylineBusy", Reflect.All)!),
            (Target: (object?)null, Field: GraylineFlow.GetType().Assembly.GetType("AugMechanicalAscension.Integration.NicoWorkshop", true)!.GetField("_busy", Reflect.All)!),
            (Target: (object?)null, Field: Reflect.Type("Nicokobo.ContentInterop.NativeWorkshop", "Nicokobo.Forge").GetField("_busy", Reflect.All)!)
        };
        object?[] previous = flags.Select(flag => flag.Field.GetValue(flag.Target)).ToArray();
        string marker = O.RunId + ".failed-purchase-save";
        try
        {
            foreach (var flag in flags) flag.Field.SetValue(flag.Target, true);
            ModHelper.SetModData("nicokobo.content_native_probe", "failure-save", marker);
            Store.SaveGame();
            string file = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId));
            var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", new ES3Settings(file, ES3.Location.File))!;
            GraylineRequire("grayline.busy-mods-cannot-veto-save", "Actual ES3 contains the new marker, exact current cash and full modData including pending purchase",
                new { file, saved.playerCash, modData = saved.ModData(), inputFlags = flags.Select(flag => flag.Field.DeclaringType!.FullName + "." + flag.Field.Name).ToArray() },
                saved.playerCash == Store.playerCash &&
                saved.ModData().TryGetValue("nicokobo.content_native_probe:failure-save", out string value) && value == marker &&
                JToken.DeepEquals(Reflect.Json(saved.ModData()), Reflect.Json(Store.ModData())));
        }
        finally
        {
            for (int i = 0; i < flags.Length; i++) flags[i].Field.SetValue(flags[i].Target, previous[i]);
        }
    }

    private sealed class GraylinePurchaseFault : IDisposable
    {
        private static GraylinePurchaseFault? _active;
        private readonly Harmony _harmony;
        private readonly GameItem _item;
        internal readonly string Marker;
        internal int Hits;

        internal GraylinePurchaseFault(string runId, GameItem item)
        {
            _item = item;
            Marker = "Expected Grayline native purchase interruption " + runId;
            _harmony = new Harmony("nicokobo.content_native_probe.grayline_fault." + runId);
            _active = this;
            try
            {
                _harmony.Patch(AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.OnItemBought), [typeof(GameItem), typeof(int)]),
                    prefix: new HarmonyMethod(typeof(GraylinePurchaseFault), nameof(BeforeBought)) { priority = Priority.First });
            }
            catch { Dispose(); throw; }
        }

        private static void BeforeBought(GameItem __0)
        {
            if (_active is not { } fault || !ReferenceEquals(__0, fault._item)) return;
            fault.Hits++;
            throw new InvalidOperationException(fault.Marker);
        }

        public void Dispose()
        {
            _active = null;
            _harmony.UnpatchAll(_harmony.Id);
        }
    }
}
