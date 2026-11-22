using AugMechanicalAscension.Serialization;

namespace AugMechanicalAscension.Domain;

internal sealed record WorkshopReward(string ItemId, int Count, string Chinese, string English);
internal sealed record WorkshopAchievement(string Id, string? ProofItemId, string Chinese,
    string English, string? Prerequisite, WorkshopReward[] Rewards);
internal sealed record WorkshopOwnedItem(string ItemId, int UniqueId, int Units, bool Owned);
internal sealed record WorkshopProof(string ItemId, int UniqueId, int Day);
internal sealed record WorkshopRewardReceipt(string ItemId, int UniqueId, int Units,
    int? FullEnergy = null);
internal sealed record WorkshopPendingReward(string NodeId, int CashBefore,
    WorkshopRewardReceipt[] Items);
internal sealed record WorkshopAchievementState
{
    public int SchemaVersion { get; init; } = 1;
    public string RunId { get; init; } = "";
    public int SlotId { get; init; }
    public Dictionary<string, WorkshopProof> Completed { get; init; } = new(StringComparer.Ordinal);
    public string[] Claimed { get; init; } = [];
    public WorkshopPendingReward? Pending { get; init; }
    public bool DebugAscensionUnlocked { get; init; }
    // Null is a record from before mixed rewards; its final claim granted two interfaces.
    public int? GrantedNeuralInterfaces { get; init; }
}

// Content-owned goals; neither achievement records nor reward claims enable an item effect.
internal static class WorkshopAchievements
{
    internal const string SaveKey = "nicokobo.aug.workshop.achievements";
    internal const string AscensionId = "nicokobo.aug.mechanical_ascension";
    internal const string BatteryId = "energy_credit_ext";
    internal const string SelfChargingBatteryId = "energy_credit_breeder";
    internal static bool IsBattery(string id) => id is BatteryId or SelfChargingBatteryId;
    private const string Synthesis = "nicokobo.mechcore.synthesis.";
    private const string ModuleMatrix = "nicokobo.mechcore_protocol.module_matrix.";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 16
    };
    internal static readonly WorkshopAchievement[] Definitions =
    [
        N(CyberneticRules.InfraredVision,
            CyberneticProductDefinitions.ItemId("infrared_vision"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.infrared_vision", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.infrared_vision", "en"),
            null,
            new WorkshopReward("metal_scanner",
                1,
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.scav_scanner", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.scav_scanner", "en"))),
        N(CyberneticRules.CyberEye,
            Synthesis + "cyber_eye",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.high_tech_glasses", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.high_tech_glasses", "en"),
            CyberneticRules.InfraredVision,
            Battery()),
        N(CyberneticRules.ArtificialAnkle,
            CyberneticProductDefinitions.ItemId("artificial_ankle"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.artificial_ankle", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.artificial_ankle", "en"),
            null,
            new WorkshopReward("welder",
                1,
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.plasma_cutter", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.plasma_cutter", "en"))),
        N(CyberneticRules.SleepControl,
            Synthesis + "sleep_compression_module",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.sleep_compression_module", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.sleep_compression_module", "en"),
            CyberneticRules.ArtificialAnkle,
            new WorkshopReward("turbo_booster_adv",
                1,
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.advanced_turbo_booster", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.advanced_turbo_booster", "en"))),
        N(CyberneticRules.CommunicationReceiver,
            CyberneticProductDefinitions.ItemId("communication_receiver"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.holographic_communicator", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.holographic_communicator", "en"),
            CyberneticRules.SleepControl,
            new WorkshopReward(SelfChargingBatteryId,
                1,
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.self_charging_energy_cell", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.self_charging_energy_cell", "en"))),
        N(CyberneticRules.TitaniumSkeleton,
            Synthesis + "titanium_exoskeleton",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.titanium_exoskeleton", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.titanium_exoskeleton", "en"),
            null,
            new WorkshopReward(ModuleMatrix + "performance_conversion_node",
                1,
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.performance_conversion_node", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.performance_conversion_node", "en"))),
        N(CyberneticRules.KineticRecovery,
            Synthesis + "kinetic_recovery",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.kinetic_recovery", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.kinetic_recovery", "en"),
            CyberneticRules.TitaniumSkeleton,
            new WorkshopReward(ModuleMatrix + "efficiency_conversion_node",
                1,
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.efficiency_conversion_node", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.efficiency_conversion_node", "en"))),
        N(CyberneticRules.FineMotorAssistant,
            Synthesis + "fine_motor_assistant",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.fine_motor_assistant", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.fine_motor_assistant", "en"),
            CyberneticRules.KineticRecovery,
            new WorkshopReward(ModuleMatrix + "quality_conversion_node",
                1,
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.quality_conversion_node", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.quality_conversion_node", "en"))),
        N(CyberneticRules.SyntheticVocalCords,
            CyberneticProductDefinitions.ItemId("synthetic_vocal_cords"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.synthetic_vocal_cords", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.synthetic_vocal_cords", "en"),
            null,
            Surgery()),
        N(CyberneticRules.MimeticSkin,
            CyberneticProductDefinitions.ItemId("mimetic_skin"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.mimetic_skin", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.mimetic_skin", "en"),
            CyberneticRules.SyntheticVocalCords,
            Surgery()),
        N(CyberneticRules.AiAssistantCore,
            Synthesis + "ai_core",
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.ai_core", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.ai_core", "en"),
            CyberneticRules.MimeticSkin,
            Neural(1)),
        N(CyberneticRules.ArtificialHeart,
            CyberneticProductDefinitions.ItemId("artificial_heart"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.artificial_heart", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.artificial_heart", "en"),
            CyberneticRules.AiAssistantCore,
            Neural(1)),
        N(AscensionId,
            null,
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.mechanical_ascension", "zh"),
            Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.mechanical_ascension", "en"),
            null,
            Neural(1),
            new WorkshopReward("system_uncapped_neural_core",
                1,
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.neural_core_module_uncapped", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.neural_core_module_uncapped", "en")))
    ];
    internal static readonly string[] BranchEnds =
    [CyberneticRules.CyberEye, CyberneticRules.CommunicationReceiver,
     CyberneticRules.FineMotorAssistant, CyberneticRules.ArtificialHeart];

    internal static WorkshopAchievement? Definition(string id)
        => Definitions.FirstOrDefault(x => x.Id == id);
    internal static bool Relevant(string itemId) => Definitions.Any(x => x.ProofItemId == itemId);
    internal static int ClaimedCybernetics(WorkshopAchievementState state)
        => Definitions.Count(x => x.ProofItemId != null && state.Claimed.Contains(x.Id));
    // Pending manifests are staged only after the whole reward is placed.
    // Count them before commit too, so save confirmation cannot raise the price.
    internal static int DeliveredNeuralInterfaces(WorkshopAchievementState state)
        => ClaimedNeuralInterfaces(state) +
           (state.Pending?.Items.Where(x => x.ItemId == NeuralModuleRules.ItemId).Sum(x => x.Units) ?? 0);
    private static int ClaimedNeuralInterfaces(WorkshopAchievementState state)
        => state.GrantedNeuralInterfaces ??
           (state.Claimed.Contains(CyberneticRules.AiAssistantCore) ? 1 : 0) +
           (state.Claimed.Contains(CyberneticRules.ArtificialHeart) ? 1 : 0) +
           (state.Claimed.Contains(AscensionId) ? 2 : 0);
    internal static bool Completed(WorkshopAchievementState state, string id)
        => id == AscensionId ? state.DebugAscensionUnlocked || ClaimedCybernetics(state) == 12 : state.Completed.ContainsKey(id);
    internal static bool PrerequisitesMet(WorkshopAchievementState state, string id)
        => Definition(id) is { } definition && (id == AscensionId
            ? state.DebugAscensionUnlocked || ClaimedCybernetics(state) == 12
            : definition.Prerequisite == null || state.Claimed.Contains(definition.Prerequisite));
    internal static bool CanClaim(WorkshopAchievementState state, string id)
        => state.Pending == null && !state.Claimed.Contains(id) && Definition(id) != null &&
           !(id == AscensionId && state.DebugAscensionUnlocked) &&
           Completed(state, id) && PrerequisitesMet(state, id);

    internal static WorkshopAchievementState UnlockAscensionForDebug(WorkshopAchievementState state)
    {
        Validate(state);
        if (state.Pending != null) throw new InvalidOperationException("Reward delivery is pending");
        // Keep real claims intact: they alone count as delivered reward items.
        return state.Claimed.Contains(AscensionId) ? state : state with { DebugAscensionUnlocked = true };
    }

    // The caller supplies a committed live or restored record; pending delivery
    // does not count as a final claim.
    internal static bool IsMechanicallyAscended(WorkshopAchievementState? state,
        string runId, int slotId)
        => state != null && state.SlotId == slotId &&
           Guid.TryParse(state.RunId, out var recordedRun) &&
           Guid.TryParse(runId, out var currentRun) && recordedRun == currentRun &&
           (state.DebugAscensionUnlocked || state.Claimed.Contains(AscensionId));

    internal static WorkshopAchievementState Latch(WorkshopAchievementState state,
        IEnumerable<WorkshopOwnedItem> items, int day)
    {
        if (day < 0 || day > AugNumbers.Validation.MaxGameDay) throw new ArgumentOutOfRangeException(nameof(day));
        var completed = new Dictionary<string, WorkshopProof>(state.Completed, StringComparer.Ordinal);
        var owned = items.Where(x => x.Owned && x.Units > 0 && x.UniqueId > 0 && Relevant(x.ItemId))
            .GroupBy(x => x.UniqueId).Where(x => x.Select(i => i.ItemId).Distinct().Count() == 1)
            .Select(x => x.First()).OrderBy(x => x.UniqueId).ToArray();
        foreach (var definition in Definitions.Where(x => x.ProofItemId != null))
            if (!completed.ContainsKey(definition.Id) && owned.FirstOrDefault(x =>
                    x.ItemId == definition.ProofItemId && completed.Values.All(p => p.UniqueId != x.UniqueId)) is { } item)
                completed.Add(definition.Id, new(item.ItemId, item.UniqueId, day));
        return state with { Completed = completed };
    }

    internal static WorkshopAchievementState Prepare(WorkshopAchievementState state,
        string nodeId, int cashBefore, WorkshopRewardReceipt[] receipts)
    {
        if (!CanClaim(state, nodeId) || cashBefore < 0) throw new InvalidOperationException("Achievement is not claimable");
        var rewards = Definition(nodeId)!.Rewards;
        if (receipts == null || receipts.Length != rewards.Sum(reward => reward.Count) ||
            rewards.Any(reward => receipts.Count(item => item != null && item.ItemId == reward.ItemId) != reward.Count))
            throw new InvalidOperationException("Reward manifest does not match the achievement");
        var next = state with { Pending = new(nodeId, cashBefore, receipts.ToArray()) };
        Validate(next);
        return next;
    }

    // Called after whole-bundle placement is verified, or after a restored
    // pending manifest and its reward UIDs are verified from the native save.
    internal static WorkshopAchievementState Commit(WorkshopAchievementState state)
    {
        Validate(state);
        if (state.Pending is not { } pending) throw new InvalidOperationException("No reward delivery to commit");
        var next = state with { Claimed = [.. state.Claimed, pending.NodeId], Pending = null,
            GrantedNeuralInterfaces = DeliveredNeuralInterfaces(state) };
        Validate(next);
        return next;
    }

    internal static string Encode(WorkshopAchievementState state)
    {
        Validate(state);
        return JsonSerializer.Serialize(state, JsonOptions);
    }
    internal static WorkshopAchievementState Decode(string json, string runId, int slotId)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 32768) throw new InvalidOperationException("Invalid achievement record size");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        ValidateKeys(document.RootElement);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            new[] { "schemaVersion", "runId", "slotId", "completed", "claimed", "pending" }
                .Any(key => !document.RootElement.TryGetProperty(key, out _)))
            throw new InvalidOperationException("Missing achievement state field");
        var state = JsonSerializer.Deserialize<WorkshopAchievementState>(json, JsonOptions)
            ?? throw new InvalidOperationException("Missing achievement record");
        Validate(state);
        if (!Guid.TryParse(runId, out var expected) || !Guid.TryParse(state.RunId, out var actual) ||
            expected != actual || slotId != state.SlotId) throw new InvalidOperationException("Achievement record belongs to another run or slot");
        return state;
    }
    internal static void Validate(WorkshopAchievementState state)
    {
        if (state.SchemaVersion != 1 || !Guid.TryParse(state.RunId, out var run) || run == Guid.Empty ||
            state.SlotId < 0 || state.Completed == null || state.Claimed == null ||
            state.Completed.Count > 12 || state.Claimed.Length > 13 ||
            state.Claimed.Distinct(StringComparer.Ordinal).Count() != state.Claimed.Length)
            throw new InvalidOperationException("Invalid achievement state");
        var uids = new HashSet<int>();
        foreach (var (id, proof) in state.Completed)
            if (Definition(id) is not { ProofItemId: not null } definition || proof == null ||
                proof.ItemId != definition.ProofItemId || proof.UniqueId <= 0 || !uids.Add(proof.UniqueId) ||
                proof.Day is < 0 or > AugNumbers.Validation.MaxGameDay)
                throw new InvalidOperationException("Invalid owned-item achievement proof");
        foreach (string id in state.Claimed)
            if (Definition(id) == null || !Completed(state, id) || !PrerequisitesMet(state, id) ||
                (id == AscensionId && ClaimedCybernetics(state) != 12))
                throw new InvalidOperationException("Invalid claimed achievement or prerequisite");
        int currentCount = Definitions.Where(x => state.Claimed.Contains(x.Id)).SelectMany(x => x.Rewards)
            .Where(x => x.ItemId == NeuralModuleRules.ItemId).Sum(x => x.Count);
        if (state.GrantedNeuralInterfaces is { } granted &&
            (granted < currentCount || granted > currentCount + (state.Claimed.Contains(AscensionId) ? 1 : 0)))
            throw new InvalidOperationException("Invalid granted interface count");
        if (state.Pending is not { } pending) return;
        var withoutPending = state with { Pending = null };
        if (!CanClaim(withoutPending, pending.NodeId) || pending.CashBefore < 0 || pending.Items == null)
            throw new InvalidOperationException("Invalid pending reward");
        var rewardUids = new HashSet<int>();
        // Saved receipts describe a delivery already made, even if the catalog changed.
        // Prepare validates new bundles; recovery only verifies these original identities.
        if (pending.Items.Length is < 1 or > 32 || pending.Items.Any(item => item == null ||
            string.IsNullOrWhiteSpace(item.ItemId) || item.Units != 1 || item.UniqueId <= 0 || !rewardUids.Add(item.UniqueId) ||
            uids.Contains(item.UniqueId) || (IsBattery(item.ItemId)
                ? item.FullEnergy is null or <= 0 : item.FullEnergy != null)))
            throw new InvalidOperationException("Reward manifest does not match the achievement");
    }
    private static void ValidateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new InvalidOperationException("Duplicate achievement JSON key");
                ValidateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateKeys(item);
    }
    private static WorkshopAchievement N(string id, string? proofId, string zh, string en,
        string? prerequisite, params WorkshopReward[] rewards) => new(id, proofId, zh, en, prerequisite, rewards);
    private static WorkshopReward Surgery() => new("surgery_tool",
        1,
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.surgery_toolset", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.surgery_toolset", "en"));
    private static WorkshopReward Battery() => new(BatteryId,
        1,
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.energy_battery_mk_ii", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.energy_battery_mk_ii", "en"));
    private static WorkshopReward Neural(int count) => new(NeuralModuleRules.ItemId,
        count,
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.brain_computer_interface", "zh"),
        Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.brain_computer_interface", "en"));
}
