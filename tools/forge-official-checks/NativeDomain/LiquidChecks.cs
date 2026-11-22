using System.Diagnostics;
using Newtonsoft.Json;
using Nicokobo.ContentInterop;

internal static class LiquidChecks
{
    private const string Tag = "NICOKOBO_FORGE_LIQUID_VALUES_V1";
    private static void Expect(bool value, string reason) => Program.Expect(value, reason);
    private static GameItem Item(params int[] parts)
    {
        var item = new GameItem();
        SetParts(item, parts);
        return item;
    }
    private static void SetParts(GameItem item, params int[] parts)
    {
        for (int i = 0; i < Liquid.Liquids.Count; i++)
        {
            string tag = Liquid.Liquids[i].currentPart;
            item.state!.dict[tag] = new(tag, tag) { valueInt = i < parts.Length ? parts[i] : 0 };
        }
    }
    private static NativeMachineLiquidSnapshot Raw(GameItem item) => NativeLiquidApi.CaptureRaw(item)!;
    private static NativeMachineLiquidSnapshot Captured(GameItem item) => NativeLiquidApi.Capture(item)!;
    private static NativeMachineLiquidSnapshot Valued(GameItem item) => Raw(item) with
    {
        Contents = Raw(item).Contents.Select((part, i) => part.Parts == 0 ? part : part with
        { Value = i == 0 ? 100m : 70m, QualityBasis = i == 0 ? 40m : 20m }).ToArray()
    };
    private static bool Same(NativeMachineLiquidSnapshot left, NativeMachineLiquidSnapshot right) =>
        left.CapacityParts == right.CapacityParts && left.Contents.SequenceEqual(right.Contents);
    private static void Hook(string name, params object?[] args) =>
        typeof(NativeLiquidValueRuntime).GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, args);
    internal static void Run()
    {
        var item = Item(100_000, 50_000);
        var expected = Valued(item);
        NativeLiquidValueRuntime.Store(item, expected);
        string original = item.GetTagReadonly(Tag)!.valueString;
        int syncs = item.Syncs;
        for (int i = 0; i < 10; i++) NativeLiquidValueRuntime.RefreshValue(item);
        Expect(Same(Captured(item), expected) && item.unitValue == 170, "Stable reads changed component or total values");
        Expect(item.Syncs == syncs && item.GetTagReadonly(Tag)!.valueString == original, "Stable reads rewrote the native tag");
        item.unitValue = 1;
        NativeLiquidValueRuntime.RefreshValue(item);
        Expect(item.unitValue == 170, "External unit value change was not corrected");

        // Another container with the same values and a different capacity must
        // keep its own shape; two containers with one UID may never alias state.
        var other = Item(100_000, 50_000); other.uniqueId = item.uniqueId; other.Capacity = 2_000_000;
        other.state!.dict[Tag] = new(Tag, Tag) { valueString = original };
        NativeLiquidValueRuntime.RefreshValue(other);
        Expect(Captured(other).CapacityParts == 2_000_000 && other.unitValue == 170, "Cache ignored capacity or reused an item identity");
        Expect(Captured(item).CapacityParts == 1_000_000, "Other container changed the first snapshot");

        // Raw tag edits bypass normal mutation hooks. Every capture still reads
        // every live component and reconciles against its exact ledger string.
        SetParts(item, 50_000, 100_000, 25_000);
        NativeLiquidValueRuntime.RefreshValue(item);
        var changed = Captured(item);
        Expect(changed.Contents[0].Value == 50m && changed.Contents[0].QualityBasis == 20m, "Consumption did not scale water value/basis");
        Expect(changed.Contents[1].Value == 72m && changed.Contents[1].QualityBasis == 22m, "Dilution did not append native oil value/basis");
        Expect(changed.Contents[2].Value == null && changed.TotalParts == 175_000 && item.unitValue == 123, "Unvalued new component or total was lost");
        Expect(Same(changed, LiquidValueLedger.Decode(item.GetTagReadonly(Tag)!.valueString, Raw(item))), "Reconciled ledger was not normalized back to the tag");

        // External valid JSON and legacy payloads (without QualityBasis) win
        // immediately, even if their liquid amounts match the previous read.
        item.state!.dict[Tag].valueString = "[{\"LiquidId\":\"water\",\"Parts\":50000,\"Value\":7}]";
        NativeLiquidValueRuntime.RefreshValue(item);
        var legacy = Captured(item);
        Expect(legacy.Contents[0].Value == 7 && legacy.Contents[0].QualityBasis == null && item.unitValue == 12, "Old ledger or external tag edit was ignored");
        string saved = item.GetTagReadonly(Tag)!.valueString;
        var reloaded = Item(50_000, 100_000, 25_000);
        reloaded.state!.dict[Tag] = new(Tag, Tag) { valueString = saved };
        NativeLiquidValueRuntime.RefreshValue(reloaded);
        Expect(Same(Captured(reloaded), legacy), "Save/reload changed ledger values");

        item.state!.dict[Tag].valueString = "[{\"LiquidId\":\"missing\",\"Parts\":1,\"Value\":2}]";
        Program.Reject(() => Captured(item), "Unknown externally edited component was hidden by cache");
        item.state.dict[Tag].valueString = "[{\"LiquidId\":\"water\",\"Parts\":1,\"Value\":-1}]";
        Program.Reject(() => Captured(item), "Negative externally edited value was accepted");
        item.state.dict[Tag].valueString = "[{\"LiquidId\":\"water\",\"Parts\":1,\"Value\":2},{\"LiquidId\":\"water\",\"Parts\":1,\"Value\":2}]";
        Program.Reject(() => Captured(item), "Duplicate externally edited component was accepted");
        item.state.dict[Tag].valueString = "[";
        Program.Reject(() => Captured(item), "Malformed external JSON was accepted");
        item.state.dict[Tag].valueString = "";
        Expect(Captured(item).Contents.All(part => part.Value == null), "Removed ledger survived in cache");

        // Store must own an immutable value copy, rather than a caller's list.
        var mutableItem = Item(100_000);
        var mutable = Valued(mutableItem).Contents.ToArray();
        var snapshot = new NativeMachineLiquidSnapshot(mutableItem.Capacity, mutable);
        NativeLiquidValueRuntime.Store(mutableItem, snapshot);
        mutable[0] = mutable[0] with { Value = 222m };
        NativeLiquidValueRuntime.Store(mutableItem, snapshot);
        Expect(Captured(mutableItem).Contents[0].Value == 222m && mutableItem.unitValue == 222, "Caller mutation was hidden by an aliased cache snapshot");
        mutable[0] = mutable[0] with { QualityBasis = 17m };
        NativeLiquidValueRuntime.Store(mutableItem, snapshot);
        Expect(Captured(mutableItem).Contents[0].QualityBasis == 17m && mutableItem.unitValue == 222,
            "Quality-basis-only change was lost while total value stayed equal");

        var source = Item(100_000, 50_000); var target = Item(25_000, 25_000);
        NativeLiquidValueRuntime.Store(source, Valued(source)); NativeLiquidValueRuntime.Store(target, Valued(target));
        var from = Captured(source); var to = Captured(target);
        decimal total = MachineBatchMath.BaseValue(from) + MachineBatchMath.BaseValue(to);
        decimal basis = MachineBatchMath.QualityBasis(from) + MachineBatchMath.QualityBasis(to);
        object?[] transfer = [source, target, null]; Hook("TransferPrefix", transfer);
        SetParts(source, 75_000, 25_000); SetParts(target, 50_000, 50_000);
        Hook("TransferPostfix", source, target, transfer[2]);
        Expect(MachineBatchMath.BaseValue(Captured(source)) + MachineBatchMath.BaseValue(Captured(target)) == total, "Pouring lost total value");
        Expect(MachineBatchMath.QualityBasis(Captured(source)) + MachineBatchMath.QualityBasis(Captured(target)) == basis, "Pouring lost quality basis");
        Expect(Captured(source).TotalParts + Captured(target).TotalParts == 200_000, "Pouring lost a liquid component");
        object?[] add = [source, null]; Hook("ChangePrefix", add);
        SetParts(source, 100_000, 25_000); Hook("ChangePostfix", source, add[1]);
        Expect(Captured(source).Contents[0].Value == 76m && Captured(source).Contents[0].QualityBasis == 31m, "Hooked dilution did not preserve prior value/basis");
        SetParts(source, 0, 0, 0); Hook("EmptyPostfix", source);
        Expect(source.GetTagReadonly(Tag)!.valueString == "" && source.unitValue == 0 && Captured(source).Contents.All(part => part.Value == null), "Emptying retained a ledger or value");

        // Equality must include zero-volume components and their ordering too.
        var shape = Item(100_000, 0, 0); NativeLiquidValueRuntime.Store(shape, Valued(shape)); Captured(shape);
        Liquid.Liquids.Reverse();
        try { Expect(Captured(shape).Contents[2].LiquidId == "water" && Captured(shape).Contents[2].Value == 100m, "Cache ignored a changed liquid directory order"); }
        finally { Liquid.Liquids.Reverse(); }

        var first = Item(100_000, 50_000); var second = Item(100_000, 25_000);
        NativeLiquidValueRuntime.Store(first, Valued(first));
        NativeLiquidValueRuntime.Store(second, Valued(second));
        NativeLiquidValueRuntime.RefreshValue(first); NativeLiquidValueRuntime.RefreshValue(second);
        var counter = new LedgerCounter();
        var settings = JsonConvert.DefaultSettings;
        JsonConvert.DefaultSettings = () => new JsonSerializerSettings { Converters = { counter } };
        try
        {
            for (int i = 0; i < 100; i++) NativeLiquidValueRuntime.RefreshValue(i % 2 == 0 ? first : second);
            Expect(counter.Encodes == 0 && counter.Decodes == 0,
                $"Stable alternating containers repeated JSON work: encode={counter.Encodes}; decode={counter.Decodes}");
            Expect(first.unitValue == 170 && second.unitValue == 170, "Alternating containers changed their stored values");
        }
        finally { JsonConvert.DefaultSettings = settings; }
        for (int i = 0; i < 20; i++)
        {
            var evictionItem = Item(100_000 + i * 1000, 50_000);
            NativeLiquidValueRuntime.Store(evictionItem, Valued(evictionItem)); Captured(evictionItem);
        }
        NativeLiquidValueRuntime.RefreshValue(first);
        Expect(Same(Captured(first), Valued(first)) && first.unitValue == 170,
            "Liquid cache eviction changed a container's composition or values");
    }
    internal static void Measure()
    {
        Console.WriteLine("Offline .NET " + Environment.Version + "; production Capture/Read/Store; native/Unity tag ports; not a Unity profiler result.");
        var item = Item(100_000, 50_000);
        NativeLiquidValueRuntime.Store(item, Valued(item));
        for (int i = 0; i < 30; i++) NativeLiquidValueRuntime.RefreshValue(item);
        var timings = new List<double>();
        for (int sample = 0; sample < 5; sample++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) NativeLiquidValueRuntime.RefreshValue(item);
            timer.Stop(); long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            timings.Add(timer.Elapsed.TotalMilliseconds);
            Console.WriteLine($"valued 1000x sample={sample + 1}; allocated={allocated}; elapsed_ms={timer.Elapsed.TotalMilliseconds:F3}; total={item.unitValue}");
        }
        timings.Sort(); Console.WriteLine($"valued median_ms={timings[2]:F3}");
        var converter = new LedgerCounter();
        JsonConvert.DefaultSettings = () => new JsonSerializerSettings { Converters = { converter } };
        try
        {
            for (int i = 0; i < 1000; i++) NativeLiquidValueRuntime.RefreshValue(item);
            Console.WriteLine($"valued 1000x instrumented; Encode={converter.Encodes}; Decode={converter.Decodes}");
        }
        finally { JsonConvert.DefaultSettings = null; }
        var second = Item(100_000, 25_000);
        NativeLiquidValueRuntime.Store(second, Valued(second));
        NativeLiquidValueRuntime.RefreshValue(item); NativeLiquidValueRuntime.RefreshValue(second);
        converter.Encodes = converter.Decodes = 0;
        JsonConvert.DefaultSettings = () => new JsonSerializerSettings { Converters = { converter } };
        try
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) NativeLiquidValueRuntime.RefreshValue(i % 2 == 0 ? item : second);
            timer.Stop();
            Console.WriteLine($"alternating 1000x; Encode={converter.Encodes}; Decode={converter.Decodes}; allocated={GC.GetAllocatedBytesForCurrentThread() - before}; elapsed_ms={timer.Elapsed.TotalMilliseconds:F3}");
        }
        finally { JsonConvert.DefaultSettings = null; }
        var water = Item(100_000);
        long waterBefore = GC.GetAllocatedBytesForCurrentThread();
        var waterTime = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) NativeLiquidValueRuntime.RefreshValue(water);
        waterTime.Stop();
        Console.WriteLine($"plain_water 1000x; allocated={GC.GetAllocatedBytesForCurrentThread() - waterBefore}; elapsed_ms={waterTime.Elapsed.TotalMilliseconds:F3}");
    }
    private sealed class LedgerCounter : JsonConverter
    {
        private bool _inside;
        internal int Encodes, Decodes;
        public override bool CanConvert(Type objectType) => !_inside && objectType == typeof(NativeMachineLiquidPart[]);
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            Encodes++; _inside = true;
            try { serializer.Serialize(writer, value); } finally { _inside = false; }
        }
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            Decodes++; _inside = true;
            try { return serializer.Deserialize<NativeMachineLiquidPart[]>(reader); } finally { _inside = false; }
        }
    }
}
