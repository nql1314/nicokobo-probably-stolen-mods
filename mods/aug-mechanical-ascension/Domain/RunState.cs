namespace AugMechanicalAscension.Domain;

// Managed-only state. No Unity object or game wrapper may be serialized here.
internal sealed record ConfigSnapshot
{
    public int InitialCredits { get; init; } = AugNumbers.OpeningAndSurvival.InitialCredits;
    public int InitialRent { get; init; } = AugNumbers.OpeningAndSurvival.InitialRent;
    // Opening adjustment to native reputation; keep the existing JSON key.
    public int InitialSecurityReputation { get; init; } = AugNumbers.OpeningAndSurvival.InitialSecurityReputation;
    // Units added by this Mod after the native opening items have been read.
    public int InitialBatteryCount { get; init; } = AugNumbers.OpeningAndSurvival.InitialBatteryCount;
    public int InitialMedicalCount { get; init; } = AugNumbers.OpeningAndSurvival.InitialMedicalCount;
    public int InitialElectronicCount { get; init; } = AugNumbers.OpeningAndSurvival.InitialElectronicCount;
    public int InitialHealth { get; init; } = AugNumbers.OpeningAndSurvival.InitialHealth;
    public int MaxHealth { get; init; } = AugNumbers.OpeningAndSurvival.MaxHealth;
    public int DailyHealthLoss { get; init; } = AugNumbers.OpeningAndSurvival.DailyHealthLoss;
    public int InitialExposure { get; init; } = AugNumbers.OpeningAndSurvival.InitialExposure;
    public int MaxExposure { get; init; } = AugNumbers.OpeningAndSurvival.MaxExposure;
    public int DailyExposureGain { get; init; } = AugNumbers.OpeningAndSurvival.DailyExposureGain;
    public int ExposureGraceDays { get; init; } = AugNumbers.OpeningAndSurvival.ExposureGraceDays;
    // Retained snapshot fields for save validation/readback; current exposure
    // pricing tiers come from AugNumbers for new and existing runs alike.
    public int BargainThreshold1 { get; init; } = AugNumbers.OpeningAndSurvival.BargainThreshold1;
    public int BargainThreshold2 { get; init; } = AugNumbers.OpeningAndSurvival.BargainThreshold2;
    public int BargainThreshold3 { get; init; } = AugNumbers.OpeningAndSurvival.BargainThreshold3;
    public int BargainPenalty1Bp { get; init; } = AugNumbers.OpeningAndSurvival.BargainPenalty1Bp;
    public int BargainPenalty2Bp { get; init; } = AugNumbers.OpeningAndSurvival.BargainPenalty2Bp;
    public int BargainPenalty3Bp { get; init; } = AugNumbers.OpeningAndSurvival.BargainPenalty3Bp;
    public int SameKindBonusBp { get; init; } = AugNumbers.OpeningAndSurvival.SameKindBonusBp;
    public int VoiceBonusBp { get; init; } = AugNumbers.OpeningAndSurvival.VoiceBonusBp;
    public int MedicalRecoveryBudget { get; init; } = AugNumbers.OpeningAndSurvival.MedicalRecoveryBudget;
    public int ElectronicSupplyTargetStock { get; init; } = AugNumbers.OpeningAndSurvival.ElectronicSupplyTargetStock;
    public int ElectronicSupplyCycleDays { get; init; } = AugNumbers.OpeningAndSurvival.ElectronicSupplyCycleDays;
    public int SupplyPriceMultiplierBp { get; init; } = AugNumbers.OpeningAndSurvival.SupplyPriceMultiplierBp;
    public int ExposureRepairPerElectronic { get; init; } = AugNumbers.OpeningAndSurvival.ExposureRepairPerElectronic;
    public long NeuralModuleTestPrice { get; init; } = AugNumbers.OpeningAndSurvival.NeuralModuleTestPrice;
    public int AppraisalPerfectRequired { get; init; } = AugNumbers.OpeningAndSurvival.AppraisalPerfectRequired;
    public int EyeScanBatteryCost { get; init; } = AugNumbers.OpeningAndSurvival.EyeScanBatteryCost;
    // Empty until each supported model has a verified native operation adapter.
    public MachineModelProfile[] MachineProfiles { get; init; } = [];
    public CyberneticConfig CyberneticPrices { get; init; } = new();
}

internal sealed record CyberneticConfig
{
    public long SleepControl { get; init; } = AugNumbers.Cybernetics.SleepControl;
    public long ArtificialAnkle { get; init; } = AugNumbers.Cybernetics.ArtificialAnkle;
    public long InfraredVision { get; init; } = AugNumbers.Cybernetics.InfraredVision;
    public long CyberEye { get; init; } = AugNumbers.Cybernetics.CyberEye;
    public long CommunicationReceiver { get; init; } = AugNumbers.Cybernetics.CommunicationReceiver;
    public long KineticRecovery { get; init; } = AugNumbers.Cybernetics.KineticRecovery;
    public long TitaniumSkeleton { get; init; } = AugNumbers.Cybernetics.TitaniumSkeleton;
    public long FineMotorAssistant { get; init; } = AugNumbers.Cybernetics.FineMotorAssistant;
    public long SyntheticVocalCords { get; init; } = AugNumbers.Cybernetics.SyntheticVocalCords;
    public long ArtificialHeart { get; init; } = AugNumbers.Cybernetics.ArtificialHeart;
    public long MimeticSkin { get; init; } = AugNumbers.Cybernetics.MimeticSkin;
    public long AiAssistantCore { get; init; } = AugNumbers.Cybernetics.AiAssistantCore;
    public string[] UnlockMaterialItemIds { get; init; } = ["common_electronic"];
    public int UnlockMaterialCount { get; init; } = AugNumbers.Cybernetics.UnlockMaterialCount;
    [Newtonsoft.Json.JsonProperty(
        DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    public CyberneticMaterialConfig? UnlockMaterials { get; init; }
    // Optional per-unlock recipes. An omitted entry keeps the legacy one-item cost.
    public Dictionary<string, WorkshopMaterialCost[]>? UnlockRecipes { get; init; }
    public int SleepExtraNightActions { get; init; } = AugNumbers.Cybernetics.SleepExtraNightActions;
    public int AnkleExtraScavAttempts { get; init; } = AugNumbers.Cybernetics.AnkleExtraScavAttempts;
    public int InfraredWoundChanceMultiplierBp { get; init; } = AugNumbers.Cybernetics.InfraredWoundChanceMultiplierBp;
    public int MachineEfficiencyBonusBp { get; init; } = AugNumbers.Cybernetics.MachineEfficiencyBonusBp;
    public int MachinePerformanceBonusBp { get; init; } = AugNumbers.Cybernetics.MachinePerformanceBonusBp;
    public int MachineQualityBonusBp { get; init; } = AugNumbers.Cybernetics.MachineQualityBonusBp;
    public int ExposureThresholdShift { get; init; } = AugNumbers.Cybernetics.ExposureThresholdShift;
    public int ArtificialHeartHealthLossMultiplierBp { get; init; } = AugNumbers.Cybernetics.ArtificialHeartHealthLossMultiplierBp;
    public int SkinDailyExposureReduction { get; init; } = AugNumbers.Cybernetics.SkinDailyExposureReduction;
}

internal sealed record CyberneticMaterialConfig
{
    public string SleepControl { get; init; } = "common_electronic";
    public string ArtificialHeart { get; init; } = "common_electronic";
    public string MimeticSkin { get; init; } = "common_electronic";
    public string ArtificialAnkle { get; init; } = "common_electronic";
    public string InfraredVision { get; init; } = "common_electronic";
    public string CyberEye { get; init; } = "common_electronic";
    public string CommunicationReceiver { get; init; } = "common_electronic";
    public string KineticRecovery { get; init; } = "common_electronic";
    public string TitaniumSkeleton { get; init; } = "common_electronic";
    public string FineMotorAssistant { get; init; } = "common_electronic";
    public string SyntheticVocalCords { get; init; } = "common_electronic";
    public string AiAssistantCore { get; init; } = "common_electronic";
    public string MechanicalAscension { get; init; } = "common_electronic";
}

internal sealed record WorkshopMaterialReceipt(string ItemId, int UniqueId,
    int UnitsBefore, int Quantity);

internal sealed record CyberneticPurchasePending(string RequestId, string ItemId,
    long Price, long CreditsBefore, int Day, string Phase,
    long? CreditsAfter = null, string? EvidenceId = null,
    string? RecoveryReason = null,
    CyberneticPurchaseObservation? RecoveryObservation = null);
internal sealed record CyberneticPurchaseObservation(long CreditsAfter, string EvidenceId);
internal sealed record CyberneticPurchaseReceipt(string RequestId, string ItemId,
    long Price, long CreditsBefore, long CreditsAfter, int Day, string EvidenceId);
internal sealed record NetworkCyberneticUnlockReceipt(string UpgradeId, string ItemId,
    int WildFavorCost, int WildFavorAfter, int Day,
    [property: Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    int? CreditsCost = null,
    [property: Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    int? CreditsBefore = null,
    [property: Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    int? CreditsAfter = null,
    [property: Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    string? MaterialItemId = null,
    [property: Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    int? MaterialUniqueId = null,
    [property: Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    int? MaterialUnitsBefore = null,
    [property: Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    WorkshopMaterialReceipt[]? Materials = null);
internal sealed record CyberEyeDeliveryReceipt(string ItemId, int NativeUniqueId,
    int Day, string EvidenceId);
internal sealed record CyberneticState
{
    public CyberneticPurchaseReceipt[] Purchases { get; init; } = [];
    public string[] EnabledIds { get; init; } = [];
    public CyberneticPurchasePending? PendingPurchase { get; init; }
    [Newtonsoft.Json.JsonProperty(
        DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    public NetworkCyberneticUnlockReceipt[]? NetworkUnlocks { get; init; }
    [Newtonsoft.Json.JsonProperty(
        DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    public CyberEyeDeliveryReceipt? CyberEyeDelivery { get; init; }

    internal bool Purchased(string itemId) => Purchases.Any(x => x.ItemId == itemId) ||
        (NetworkUnlocks?.Any(x => x.ItemId == itemId) ?? false);
    internal bool Enabled(string itemId) => EnabledIds.Contains(itemId, StringComparer.Ordinal) ||
        (NetworkUnlocks?.Any(x => x.ItemId == itemId) ?? false);
}

internal sealed record NeuralModuleReceipt(string Source, string RequestId,
    string InstanceId, int Day, string OwnershipEvidenceId,
    long Price = 0, long? CreditsBefore = null, long? CreditsAfter = null);
internal sealed record NeuralModuleDeliveryPending(string RequestId, string Source,
    int Day, string Phase, long Price, long CreditsBefore,
    string? InstanceId = null, long? CreditsAfter = null,
    string? OwnershipEvidenceId = null, string? RecoveryReason = null);
internal sealed record NeuralModuleState
{
    public NeuralModuleReceipt[] Receipts { get; init; } = [];
    public NeuralModuleDeliveryPending? PendingDelivery { get; init; }
}
internal sealed record MachineConnection(string ModuleInstanceId,
    string ItemInstanceId, string ModelId, long Epoch, int ConnectedDay,
    string OwnershipEvidenceId);
internal sealed record MachineModelProfile(string ModelId, string AdapterEvidenceId);
internal sealed record MachineState
{
    public long NextConnectionEpoch { get; init; } = 1;
    public MachineConnection[] Connections { get; init; } = [];
}

internal sealed record AppraisalSubmissionReceipt(string ItemInstanceId,
    string CategoryId, string RequestId, int Day, bool Perfect,
    string VerificationKind, string EvidenceId, string IdentityEvidenceId);
internal sealed record AppraisalScannedItem(string ItemInstanceId,
    string CategoryId, string FirstScanRequestId, int Day,
    string IdentityEvidenceId);
internal sealed record AppraisalFact(string Key, string Value);
internal sealed record AppraisalScanResult(AppraisalFact[] Facts, long? BaseValue);
internal sealed record AppraisalScanCache(string ItemInstanceId,
    string CategoryId, string Fingerprint, string AdapterVersion,
    AppraisalScanResult Result, string RequestId, int Day,
    string BatteryInstanceId, int ChargeBefore, int ChargeAfter,
    string BatteryEvidenceId);
internal sealed record AppraisalScanReceipt(string RequestId,
    string ItemInstanceId, string CategoryId, string Fingerprint,
    string AdapterVersion, int Day, string BatteryInstanceId,
    int ChargeBefore, int ChargeAfter, string BatteryEvidenceId);
internal sealed record AppraisalBatteryObservation(string BatteryInstanceId,
    int ChargeBefore, int ChargeAfter, string EvidenceId);
internal sealed record AppraisalScanPending(string RequestId,
    string ItemInstanceId, string CategoryId, string Fingerprint,
    string AdapterVersion, AppraisalScanResult Result, int Day,
    string IdentityEvidenceId, string EyeToolInstanceId,
    string EyeToolEvidenceId, string BatteryInstanceId, int ChargeBefore,
    string BatteryOwnershipEvidenceId, int Cost, string Phase,
    AppraisalBatteryObservation? Observation = null,
    string? RecoveryReason = null);
internal sealed record AppraisalState
{
    [Newtonsoft.Json.JsonProperty(
        DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
    public bool DebugAllCategoriesUnlocked { get; init; }
    public AppraisalSubmissionReceipt[] Submissions { get; init; } = [];
    public AppraisalScannedItem[] ScannedItems { get; init; } = [];
    public AppraisalScanReceipt[] ScanReceipts { get; init; } = [];
    public AppraisalScanCache[] ScanCache { get; init; } = [];
    public AppraisalScanPending? PendingScan { get; init; }
}

internal sealed record PendingOperation
{
    public long Sequence { get; init; }
    public string OperationId { get; init; } = "";
    public string RequestId { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Phase { get; init; } = "prepared";
    public int Day { get; init; }
    public string ItemInstanceId { get; init; } = "";
    public long ExpectedValue { get; init; }
    public string? ConfirmationEvidence { get; init; }
    public string? RecoveryReason { get; init; }
}

internal sealed record CommittedOperationReceipt(long Sequence, string RequestId, string Kind,
    int Day, string ItemInstanceId, long ExpectedValue, string ConfirmationEvidence);

internal sealed record MedicalSupplyState
{
    public int PeriodDay { get; init; }
    public string ItemId { get; init; } = "";
    public string SpecificationKey { get; init; } = "";
    public int RecoveryPerUnit { get; init; }
    public int Stock { get; init; }
    public long UnitPrice { get; init; }
}

internal sealed record ElectronicSupplyState
{
    public int PeriodId { get; init; }
    public int Stock { get; init; }
    public long UnitPrice { get; init; }
}

internal sealed record SupplyPurchaseObservation(long CreditsAfter, int OwnedUnitsAfter,
    string ItemId, string EvidenceId);
internal sealed record SupplyPurchasePending(string RequestId, string Kind, string ItemId,
    int PeriodKey, int Day, int StockBefore, long UnitPrice, long CreditsBefore,
    int OwnedUnitsBefore, string Phase, long? CreditsAfter = null,
    int? OwnedUnitsAfter = null, string? EvidenceId = null,
    string? RecoveryReason = null, SupplyPurchaseObservation? RecoveryObservation = null,
    long? StandardPurchasePrice = null, string? OfferEvidenceId = null);
internal sealed record SupplyPurchaseReceipt(string RequestId, string Kind, string ItemId,
    int PeriodKey, int Day, int StockBefore, long UnitPrice, long CreditsBefore,
    long CreditsAfter, int OwnedUnitsBefore, int OwnedUnitsAfter, string EvidenceId,
    long? StandardPurchasePrice = null, string? OfferEvidenceId = null);
internal sealed record SupplyPurchaseState
{
    public SupplyPurchaseReceipt[] Receipts { get; init; } = [];
    public SupplyPurchasePending? Pending { get; init; }
}

internal sealed record RunState
{
    public const int CurrentSchemaVersion = 9;
    public const string AugMechanicalAscensionId = "nicokobo.aug.mechanical_ascension.start";
    public const string SaveKey = "nicokobo.aug.state";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string RunId { get; init; } = "";
    public string GameRunId { get; init; } = "";
    public int SaveSlotId { get; init; }
    public string StartId { get; init; } = AugMechanicalAscensionId;
    public long Revision { get; init; }
    public string BuildProfileId { get; init; } = "";
    public ConfigSnapshot ConfigSnapshot { get; init; } = new();
    public string ConfigHash { get; init; } = "";
    public string InitPhase { get; init; } = "pending";
    public OpeningJournal? Opening { get; init; }
    public int StartDay { get; init; }
    public int LastObservedDay { get; init; }
    public int LastSettledDay { get; init; }
    public int Health { get; init; }
    public int Exposure { get; init; }
    public int? ExposureDeadlineDay { get; init; }
    public string FailureReason { get; init; } = "none";
    public string? EyeToolInstanceId { get; init; }
    public MedicalSupplyState? MedicalSupply { get; init; }
    public ElectronicSupplyState? ElectronicSupply { get; init; }
    public SupplyPurchaseState SupplyPurchases { get; init; } = new();
    public CyberneticState Cybernetics { get; init; } = new();
    public NeuralModuleState NeuralModules { get; init; } = new();
    public MachineState Machines { get; init; } = new();
    public AppraisalState Appraisals { get; init; } = new();
    public long NextOperationSequence { get; init; } = 1;
    public long LastCommittedOperationSequence { get; init; }
    public string? LastCommittedRequestId { get; init; }
    public CommittedOperationReceipt? LastCommittedOperation { get; init; }
    public PendingOperation? PendingOperation { get; init; }

    internal bool HasPendingExternalOperation => PendingOperation != null ||
        SupplyPurchases?.Pending != null ||
        Cybernetics.PendingPurchase != null || NeuralModules.PendingDelivery != null ||
        Appraisals.PendingScan != null;
}
