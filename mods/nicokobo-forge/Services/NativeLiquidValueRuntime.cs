
namespace Nicokobo.ContentInterop;

// Native adapters preserve optional component values during pouring, dilution,
// consumption, emptying and loading. No content item IDs or markups live here.
internal static class NativeLiquidValueRuntime
{
    private const string Tag = "NICOKOBO_FORGE_LIQUID_VALUES_V1";
    private const string Owner = "nicokobo.forge.liquid_value";
    private static Action<string>? _log;
    private sealed record TransferState(NativeMachineLiquidSnapshot Source, NativeMachineLiquidSnapshot Target);
    private sealed record ReadCache(string Saved, NativeMachineLiquidSnapshot Raw, NativeMachineLiquidSnapshot Value);
    private sealed record EncodeCache(string Saved, NativeMachineLiquidSnapshot Value);
    // Bounded pure-value caches: no item, UID, inventory owner or Unity object
    // survives a call. Live tags and every component are still captured first.
    private const int CacheSize = 16;
    private static readonly ReadCache?[] Reads = new ReadCache?[CacheSize];
    private static readonly EncodeCache?[] Encodings = new EncodeCache?[CacheSize];
    private static int _nextRead, _nextEncoding;

    internal static bool HasLedger(GameItem item) => !string.IsNullOrEmpty(item.GetTagReadonly(Tag)?.valueString);

    internal static NativeMachineLiquidSnapshot Read(GameItem item, NativeMachineLiquidSnapshot raw)
    {
        string saved = item.GetTagReadonly(Tag)?.valueString ?? "";
        if (string.IsNullOrEmpty(saved)) return raw;
        foreach (var cached in Reads)
            if (cached != null && saved == cached.Saved && NativeLiquidApi.Matches(raw, cached.Raw))
                return cached.Value;
        var value = LiquidValueLedger.Decode(saved, raw);
        var entry = new ReadCache(saved, Freeze(raw), Freeze(value));
        Reads[_nextRead] = entry; _nextRead = (_nextRead + 1) % CacheSize;
        return entry.Value;
    }

    internal static void Store(GameItem item, NativeMachineLiquidSnapshot snapshot)
    {
        bool valued = snapshot.Contents.Any(part => part.Value != null && part.Parts > 0);
        string? existing = item.GetTagReadonly(Tag)?.valueString;
        bool previouslyValued = !string.IsNullOrEmpty(existing);
        if (!valued && !previouslyValued) return;
        string saved = "";
        EncodeCache? encoding = null;
        if (valued)
        {
            foreach (var cached in Encodings)
                if (cached != null && existing == cached.Saved && NativeLiquidApi.Matches(snapshot, cached.Value))
                { encoding = cached; break; }
            saved = encoding?.Saved ?? LiquidValueLedger.Encode(snapshot);
        }
        long target = valued ? UnitValue(snapshot) : WaterFeatureHelper.GetWaterPrice(item);
        if (valued && encoding == null)
        {
            Encodings[_nextEncoding] = new(saved, Freeze(snapshot));
            _nextEncoding = (_nextEncoding + 1) % CacheSize;
        }
        // Equality is checked before encoding on repeated stable reads. Changed
        // composition or an external tag edit still decodes and normalizes.
        if (item.unitValue == target &&
            string.Equals(existing, saved, StringComparison.Ordinal))
            return;
        var tags = item.state?.dict ?? throw new InvalidOperationException("Liquid state unavailable");
        if (!tags.TryGetValue(Tag, out var tag) || tag == null)
            tags[Tag] = tag = new TagState(Tag, Tag);
        tag.Enable(); tag.SetString(saved);
        item.SyncModifiedState();
        if (item.GetTagReadonly(Tag)?.valueString != saved)
            throw new InvalidOperationException("Liquid value ledger readback mismatch");
        item.unitValue = target;
    }

    private static NativeMachineLiquidSnapshot Freeze(NativeMachineLiquidSnapshot snapshot) =>
        snapshot with { Contents = Array.AsReadOnly(snapshot.Contents.ToArray()) };

    private static long UnitValue(NativeMachineLiquidSnapshot snapshot)
    {
        decimal value = decimal.Ceiling(MachineBatchMath.BaseValue(snapshot));
        return value <= long.MaxValue ? (long)value :
            throw new OverflowException("Liquid value too large");
    }

    internal static bool Install(Action<string> log)
    {
        _log = log;
        // Patching WaterFeatureHelper here runs its native static constructor,
        // which synchronously loads localization before Unity providers are ready.
        // Container mutations reconcile the ledger; value reads reapply it.
        return NativeHookSet.Install(Owner,
        [
            new(typeof(WaterHelper), nameof(WaterHelper.TransferLiquid), [typeof(GameItem), typeof(GameItem)],
                typeof(void), typeof(NativeLiquidValueRuntime), nameof(TransferPrefix), nameof(TransferPostfix)),
            new(typeof(WaterHelper), nameof(WaterHelper.AddLiquid), [typeof(GameItem), typeof(string), typeof(int)],
                typeof(void), typeof(NativeLiquidValueRuntime), nameof(ChangePrefix), nameof(ChangePostfix)),
            new(typeof(WaterHelper), nameof(WaterHelper.EmptyContainer), [typeof(GameItem)],
                typeof(void), typeof(NativeLiquidValueRuntime), Postfix: nameof(EmptyPostfix)),
            new(typeof(GameItem), nameof(GameItem.GetRefreshedValue), [], typeof(long),
                typeof(NativeLiquidValueRuntime), Prefix: nameof(ValuePrefix))
        ], log);
    }

    internal static void Uninstall()
    {
        NativeHookSet.Remove(Owner, _log);
        Array.Clear(Reads, 0, Reads.Length); Array.Clear(Encodings, 0, Encodings.Length);
        _nextRead = _nextEncoding = 0;
    }

    private static bool _refreshing;
    private static void ValuePrefix(GameItem __instance)
    {
        if (_refreshing) return;
        _refreshing = true;
        try { RefreshPostfix(__instance); }
        finally { _refreshing = false; }
    }
    internal static void RefreshValue(GameItem item)
    {
        if (!HasLedger(item)) return;
        var current = NativeLiquidApi.Capture(item) ?? throw new InvalidOperationException("Valued container unavailable");
        Store(item, current);
    }
    private static void RefreshPostfix(GameItem __0)
    {
        if (__0 == null || __0.Handle() == IntPtr.Zero || !HasLedger(__0)) return;
        try
        {
            RefreshValue(__0);
        }
        catch (Exception ex) { Warn("refresh", ex); }
    }
    private static void ChangePrefix(GameItem __0, out NativeMachineLiquidSnapshot? __state) =>
        __state = HasLedger(__0) ? NativeLiquidApi.Capture(__0) : null;
    private static void ChangePostfix(GameItem __0, NativeMachineLiquidSnapshot? __state)
    {
        if (__state == null) return;
        try
        {
            var current = NativeLiquidApi.CaptureRaw(__0);
            if (current != null) Store(__0, LiquidValueLedger.Reconcile(__state, current));
        }
        catch (Exception ex) { Warn("change", ex); }
    }
    private static void EmptyPostfix(GameItem __0)
    {
        if (!HasLedger(__0)) return;
        try
        {
            var current = NativeLiquidApi.CaptureRaw(__0);
            if (current != null) Store(__0, current);
        }
        catch (Exception ex) { Warn("empty", ex); }
    }
    private static void TransferPrefix(GameItem __0, GameItem __1, out TransferState? __state)
    {
        __state = null;
        if (!HasLedger(__0) && !HasLedger(__1)) return;
        var source = NativeLiquidApi.Capture(__0); var target = NativeLiquidApi.Capture(__1);
        if (source != null && target != null) __state = new(source, target);
    }
    private static void TransferPostfix(GameItem __0, GameItem __1, TransferState? __state)
    {
        if (__state == null) return;
        try
        {
            var source = NativeLiquidApi.CaptureRaw(__0); var target = NativeLiquidApi.CaptureRaw(__1);
            if (source == null || target == null)
            {
                // Native postfixes must preserve the original operation result.
                Warn("transfer", new InvalidOperationException("Transferred containers unavailable"));
                return;
            }
            var values = MachineBatchMath.TransferValues(__state.Source, source, __state.Target, target);
            Store(__0, values.Source); Store(__1, values.Target);
        }
        catch (Exception ex) { Warn("transfer", ex); }
    }
    private static void Warn(string stage, Exception ex) =>
        TryLog($"[WARN] [NicokoboNative/LiquidValue] {stage} postfix skipped: " +
            $"{ex.GetType().Name}: {ex.Message}");
    private static void TryLog(string message) { try { _log?.Invoke(message); } catch { } }
}
