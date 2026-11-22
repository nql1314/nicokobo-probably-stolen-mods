using System.Security.Cryptography;
using System.Text;
using AugMechanicalAscension.Serialization;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Persistence;

internal enum ReadStatus
{
    Missing, Ready, Corrupt, FutureVersion, RunMismatch, Unavailable,
    // Written by a schema this build retired. Distinguished from Corrupt so the
    // refusal is explicit and the existing key is never rewritten.
    LegacySchema
}

internal sealed record ReadResult(ReadStatus Status, RunState? State, string? Error);

internal static class RunStateCodec
{
    private const int MaxJsonBytes = AugNumbers.RunStateCodecLimits.MaxJsonBytes;
    // A pending eye scan must leave room for its battery receipt and cache commit.
    private const int PendingScanReserveBytes = AugNumbers.RunStateCodecLimits.PendingScanReserveBytes;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = AugNumbers.Validation.RunJsonDepth
    };

    internal static RunState CreateNew(string buildProfileId, string gameRunId, int saveSlotId, int startDay, ConfigSnapshot config)
    {
        ValidateConfig(config);
        var snapshot = config with
        {
            MachineProfiles = [.. config.MachineProfiles],
            CyberneticPrices = config.CyberneticPrices with
            {
                UnlockMaterialItemIds = [.. config.CyberneticPrices.UnlockMaterialItemIds],
                UnlockRecipes = config.CyberneticPrices.UnlockRecipes?.ToDictionary(
                    x => x.Key, x => x.Value.ToArray(), StringComparer.Ordinal)
            }
        };
        if (string.IsNullOrWhiteSpace(buildProfileId)) throw new ArgumentException("Build profile is required", nameof(buildProfileId));
        if (!Guid.TryParse(gameRunId, out var nativeRunId) || nativeRunId == Guid.Empty)
            throw new ArgumentException("Game run ID is required", nameof(gameRunId));
        if (saveSlotId < 0) throw new ArgumentOutOfRangeException(nameof(saveSlotId));
        if (startDay is < 0 or > AugNumbers.Validation.MaxGameDay) throw new ArgumentOutOfRangeException(nameof(startDay));
        return new RunState
        {
            RunId = Guid.NewGuid().ToString("D"),
            GameRunId = gameRunId,
            SaveSlotId = saveSlotId,
            BuildProfileId = buildProfileId,
            ConfigSnapshot = snapshot,
            ConfigHash = ConfigHash(snapshot),
            StartDay = startDay,
            LastObservedDay = startDay,
            LastSettledDay = startDay - 1,
            Health = snapshot.InitialHealth,
            Exposure = snapshot.InitialExposure,
            Machines = new MachineState(),
            ExposureDeadlineDay = snapshot.InitialExposure == snapshot.MaxExposure
                ? checked(startDay + snapshot.ExposureGraceDays) : null
        };
    }

    internal static string Write(RunState state)
    {
        ValidateState(state);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        int bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > MaxJsonBytes ||
            state.Appraisals.PendingScan != null &&
            bytes > MaxJsonBytes - PendingScanReserveBytes)
            throw new InvalidOperationException("Run state exceeds the save size limit");
        return json;
    }

    internal static ReadResult Read(string? json)
    {
        if (json == null) return new(ReadStatus.Missing, null, null);
        if (json.Length == 0 || json.Length > MaxJsonBytes || Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            return new(ReadStatus.Corrupt, null, "Empty or oversized run state");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = AugNumbers.Validation.RunJsonDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schemaVersion", out var version) ||
                !version.TryGetInt32(out int schemaVersion))
                return new(ReadStatus.Corrupt, null, "Missing schema version");
            if (schemaVersion > RunState.CurrentSchemaVersion)
                return new(ReadStatus.FutureVersion, null, $"Unsupported schema version {schemaVersion}");
            // Older schemas are never rewritten into the current format, and the
            // existing key is left byte-for-byte intact.
            if (schemaVersion != RunState.CurrentSchemaVersion)
                return new(ReadStatus.LegacySchema, null,
                    $"Retired schema version {schemaVersion}; only schema "
                    + $"{RunState.CurrentSchemaVersion} is readable");
            var state = JsonSerializer.Deserialize<RunState>(json, JsonOptions);
            if (state == null) return new(ReadStatus.Corrupt, null, "Null run state");
            ValidateState(state);
            return new(ReadStatus.Ready, state, null);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return new(ReadStatus.Corrupt, null, ex.Message);
        }
    }

    internal static ReadResult ReadForGame(string? json, string gameRunId, int saveSlotId)
    {
        var result = Read(json);
        if (result.Status != ReadStatus.Ready || result.State == null) return result;
        if (!Guid.TryParse(gameRunId, out var expected) ||
            !Guid.TryParse(result.State.GameRunId, out var actual) ||
            actual != expected || result.State.SaveSlotId != saveSlotId)
            return new(ReadStatus.RunMismatch, null, "State belongs to a different game run or save slot");
        return result;
    }

    internal static void ValidateState(RunState state)
    {
        if (state.SchemaVersion != RunState.CurrentSchemaVersion) throw new InvalidOperationException("Unsupported schema version");
        if (!Guid.TryParse(state.RunId, out var runId) || runId == Guid.Empty) throw new InvalidOperationException("Invalid run ID");
        if (!Guid.TryParse(state.GameRunId, out var gameRunId) || gameRunId == Guid.Empty)
            throw new InvalidOperationException("Invalid game run ID");
        if (state.SaveSlotId < 0) throw new InvalidOperationException("Invalid save slot ID");
        if (state.StartId != RunState.AugMechanicalAscensionId) throw new InvalidOperationException("Wrong start ID");
        if (state.Revision < 0) throw new InvalidOperationException("Negative revision");
        if (string.IsNullOrWhiteSpace(state.BuildProfileId)) throw new InvalidOperationException("Missing build profile");
        if (state.ConfigSnapshot == null) throw new InvalidOperationException("Missing configuration snapshot");
        ValidateConfig(state.ConfigSnapshot);
        if (state.ConfigHash != ConfigHash(state.ConfigSnapshot)) throw new InvalidOperationException("Configuration hash mismatch");
        if (state.InitPhase is not ("pending" or "applying" or "complete" or "needsRecovery"))
            throw new InvalidOperationException("Unknown initialization phase");
        if (state.NeuralModules?.Receipts == null)
            throw new InvalidOperationException("Missing neural module state");
        if (!OpeningJournalRules.Valid(state))
            throw new InvalidOperationException("Invalid opening journal");
        if (state.StartDay is < 0 or > AugNumbers.Validation.MaxGameDay || state.LastObservedDay < state.StartDay ||
            state.LastSettledDay < state.StartDay - 1 || state.LastSettledDay > state.LastObservedDay ||
            state.InitPhase != "complete" &&
            (state.LastObservedDay != state.StartDay ||
             state.LastSettledDay != state.StartDay - 1))
            throw new InvalidOperationException("Invalid day markers");
        if (state.Health < 0 || state.Health > state.ConfigSnapshot.MaxHealth)
            throw new InvalidOperationException("Invalid health");
        if (state.Exposure < 0 || state.Exposure > state.ConfigSnapshot.MaxExposure)
            throw new InvalidOperationException("Invalid exposure");
        if ((state.Exposure == state.ConfigSnapshot.MaxExposure) != state.ExposureDeadlineDay.HasValue ||
            state.ExposureDeadlineDay < 0 ||
            (state.FailureReason == "none" && state.ExposureDeadlineDay.HasValue &&
             state.ExposureDeadlineDay.Value < state.LastSettledDay))
            throw new InvalidOperationException("Invalid exposure deadline");
        if (state.FailureReason is not ("none" or "bodyCollapse" or "exposure"))
            throw new InvalidOperationException("Unknown failure reason");
        if (state.ConfigSnapshot.ExposureGraceDays == 0 &&
            state.Exposure == state.ConfigSnapshot.MaxExposure &&
            state.FailureReason == "none")
            throw new InvalidOperationException("Full exposure must end the run");
        if (state.InitPhase != "complete" &&
            (state.Health != state.ConfigSnapshot.InitialHealth ||
             state.Exposure != state.ConfigSnapshot.InitialExposure ||
             state.FailureReason != "none" ||
             state.ExposureDeadlineDay !=
                 (state.Exposure == state.ConfigSnapshot.MaxExposure
                     ? checked(state.StartDay + state.ConfigSnapshot.ExposureGraceDays)
                     : null)))
            throw new InvalidOperationException("Opening survival values changed");
        if (state.EyeToolInstanceId != null &&
            !AppraisalRules.ValidText(state.EyeToolInstanceId))
            throw new InvalidOperationException("Invalid eye-tool instance ID");
        if (state.MedicalSupply is { } medical &&
            (medical.PeriodDay < state.StartDay || medical.PeriodDay > state.LastObservedDay ||
             string.IsNullOrWhiteSpace(medical.ItemId) || medical.ItemId.Length > AugNumbers.Validation.MaxIdentifierChars ||
             string.IsNullOrWhiteSpace(medical.SpecificationKey) || medical.SpecificationKey.Length > AugNumbers.Validation.MaxIdentifierChars ||
             medical.RecoveryPerUnit <= 0 || medical.Stock is < 0 or > AugNumbers.Validation.MaxSupplyStock || medical.UnitPrice <= 0))
            throw new InvalidOperationException("Invalid medical supply state");
        if (state.ElectronicSupply is { } electronic &&
            (electronic.PeriodId < 0 ||
             electronic.PeriodId > (state.LastObservedDay - state.StartDay) / state.ConfigSnapshot.ElectronicSupplyCycleDays ||
             electronic.Stock is < 0 or > AugNumbers.Validation.MaxSupplyStock || electronic.UnitPrice <= 0))
            throw new InvalidOperationException("Invalid electronic supply state");
        ValidateSupplyPurchases(state);
        if (state.NextOperationSequence < 1 || state.LastCommittedOperationSequence < 0 ||
            state.LastCommittedOperationSequence >= state.NextOperationSequence ||
            (state.LastCommittedOperationSequence == 0) != (state.LastCommittedRequestId == null) ||
            (state.LastCommittedOperationSequence == 0) != (state.LastCommittedOperation == null) ||
            (state.LastCommittedRequestId != null &&
             (!Guid.TryParse(state.LastCommittedRequestId, out var committedRequestId) ||
              committedRequestId == Guid.Empty)))
            throw new InvalidOperationException("Invalid operation sequence");
        if (state.LastCommittedOperation is { } committed &&
            (committed.Sequence != state.LastCommittedOperationSequence ||
             committed.RequestId != state.LastCommittedRequestId ||
              committed.Kind is not (OperationJournal.MedicalConsume or
                                     OperationJournal.ExposureRepair) ||
             committed.Day < state.StartDay || committed.Day > state.LastObservedDay ||
             string.IsNullOrWhiteSpace(committed.ItemInstanceId) ||
             committed.ItemInstanceId.Length > AugNumbers.Validation.MaxIdentifierChars || committed.ExpectedValue <= 0 ||
             (committed.Kind == OperationJournal.ExposureRepair && committed.ExpectedValue != 1) ||
             string.IsNullOrWhiteSpace(committed.ConfirmationEvidence) ||
             committed.ConfirmationEvidence.Length > AugNumbers.Validation.MaxIdentifierChars))
            throw new InvalidOperationException("Invalid committed operation receipt");
        if (state.PendingOperation is { } operation &&
            (operation.Sequence <= state.LastCommittedOperationSequence ||
             operation.Sequence >= state.NextOperationSequence ||
             operation.OperationId != $"{state.RunId}:{operation.Sequence}" ||
             !Guid.TryParse(operation.RequestId, out var requestId) || requestId == Guid.Empty ||
              operation.Kind is not (OperationJournal.MedicalConsume or
                                     OperationJournal.ExposureRepair) ||
             operation.Phase is not (OperationJournal.Prepared or OperationJournal.Applying or
                                     OperationJournal.ExternalConfirmed or OperationJournal.NeedsRecovery) ||
             operation.Day < state.StartDay || operation.Day > state.LastObservedDay ||
             string.IsNullOrWhiteSpace(operation.ItemInstanceId) || operation.ItemInstanceId.Length > AugNumbers.Validation.MaxIdentifierChars ||
             operation.ExpectedValue <= 0 ||
             (operation.Kind == OperationJournal.ExposureRepair && operation.ExpectedValue != 1) ||
             (operation.Phase == OperationJournal.ExternalConfirmed &&
              (string.IsNullOrWhiteSpace(operation.ConfirmationEvidence) ||
               operation.ConfirmationEvidence.Length > AugNumbers.Validation.MaxIdentifierChars)) ||
             (operation.Phase != OperationJournal.ExternalConfirmed && operation.ConfirmationEvidence != null &&
              operation.Phase != OperationJournal.NeedsRecovery) ||
             (operation.Phase == OperationJournal.NeedsRecovery &&
              (string.IsNullOrWhiteSpace(operation.RecoveryReason) || operation.RecoveryReason.Length > AugNumbers.Validation.MaxIdentifierChars))))
            throw new InvalidOperationException("Invalid pending operation");
        ValidateCybernetics(state);
        if (!NeuralModuleRules.ValidState(state))
            throw new InvalidOperationException("Invalid neural module state");
        ValidateMachines(state);
        ValidateAppraisals(state);
    }

    private static void ValidateSupplyPurchases(RunState state)
    {
        var purchases = state.SupplyPurchases;
        if (purchases?.Receipts == null ||
            purchases.Receipts.Length > SupplyPurchaseRules.MaxReceipts)
            throw new InvalidOperationException("Invalid supply purchase collection");
        var requests = new HashSet<string>(StringComparer.Ordinal);
        var lastByPeriod = new Dictionary<(string Kind, int Period),
            SupplyPurchaseReceipt>();
        int lastDay = state.StartDay;
        foreach (var receipt in purchases.Receipts)
        {
            if (receipt == null || !ValidGuid(receipt.RequestId) ||
                !requests.Add(receipt.RequestId) ||
                receipt.Kind is not (SupplyPurchaseRules.Medical or
                    SupplyPurchaseRules.Electronic) ||
                string.IsNullOrWhiteSpace(receipt.ItemId) || receipt.ItemId.Length > AugNumbers.Validation.MaxIdentifierChars ||
                (receipt.Kind == SupplyPurchaseRules.Electronic &&
                 receipt.ItemId != SupplyRules.ElectronicItemId) ||
                receipt.Day < lastDay || receipt.Day > state.LastObservedDay ||
                receipt.PeriodKey != (receipt.Kind == SupplyPurchaseRules.Medical
                    ? receipt.Day
                    : (receipt.Day - state.StartDay) /
                      state.ConfigSnapshot.ElectronicSupplyCycleDays) ||
                receipt.StandardPurchasePrice != null ||
                receipt.OfferEvidenceId != null ||
                receipt.StockBefore is < 1 or > AugNumbers.Validation.MaxSupplyStock ||
                receipt.UnitPrice is < 1 or > int.MaxValue ||
                receipt.CreditsBefore is < 0 or > int.MaxValue ||
                receipt.CreditsAfter != receipt.CreditsBefore - receipt.UnitPrice ||
                receipt.OwnedUnitsBefore is < 0 or > AugNumbers.Validation.MaxOwnedSupplyUnits ||
                receipt.OwnedUnitsAfter != receipt.OwnedUnitsBefore + 1 ||
                !SupplyDeliveryProofRules.IsEvidenceForRequest(receipt.RequestId,
                    receipt.EvidenceId))
                throw new InvalidOperationException("Invalid supply purchase receipt");
            var key = (receipt.Kind, receipt.PeriodKey);
            if (lastByPeriod.TryGetValue(key, out var previous) &&
                (receipt.StockBefore != previous.StockBefore - 1 ||
                 receipt.ItemId != previous.ItemId ||
                 receipt.UnitPrice != previous.UnitPrice))
                throw new InvalidOperationException("Supply purchase stock history changed");
            lastByPeriod[key] = receipt;
            lastDay = receipt.Day;
        }
        if (purchases.Receipts.Any(x => x.Kind == SupplyPurchaseRules.Medical) &&
            state.MedicalSupply == null ||
            purchases.Receipts.Any(x => x.Kind == SupplyPurchaseRules.Electronic) &&
            state.ElectronicSupply == null)
            throw new InvalidOperationException("Supply purchase ledger lost its supply state");
        if (state.MedicalSupply is { } medical &&
            lastByPeriod.TryGetValue((SupplyPurchaseRules.Medical, medical.PeriodDay),
                out var lastMedical) &&
            (medical.Stock != lastMedical.StockBefore - 1 ||
             medical.ItemId != lastMedical.ItemId ||
             medical.UnitPrice != lastMedical.UnitPrice))
            throw new InvalidOperationException("Medical stock disagrees with purchase ledger");
        if (state.ElectronicSupply is { } electronic &&
            lastByPeriod.TryGetValue((SupplyPurchaseRules.Electronic, electronic.PeriodId),
                out var lastElectronic) &&
            (electronic.Stock != lastElectronic.StockBefore - 1 ||
             electronic.UnitPrice != lastElectronic.UnitPrice))
            throw new InvalidOperationException("Electronic stock disagrees with purchase ledger");
        if (purchases.Pending is not { } pending) return;
        if (!ValidGuid(pending.RequestId) || requests.Contains(pending.RequestId) ||
            pending.Kind is not (SupplyPurchaseRules.Medical or
                SupplyPurchaseRules.Electronic) ||
            string.IsNullOrWhiteSpace(pending.ItemId) || pending.ItemId.Length > AugNumbers.Validation.MaxIdentifierChars ||
            (pending.Kind == SupplyPurchaseRules.Electronic &&
             pending.ItemId != SupplyRules.ElectronicItemId) ||
            pending.Day != state.LastObservedDay ||
            pending.PeriodKey != (pending.Kind == SupplyPurchaseRules.Medical
                ? pending.Day : (pending.Day - state.StartDay) /
                  state.ConfigSnapshot.ElectronicSupplyCycleDays) ||
            pending.StandardPurchasePrice != null ||
            pending.OfferEvidenceId != null ||
            pending.StockBefore is < 1 or > AugNumbers.Validation.MaxSupplyStock ||
            pending.UnitPrice is < 1 or > int.MaxValue ||
            pending.CreditsBefore is < 0 or > int.MaxValue ||
            pending.CreditsBefore < pending.UnitPrice ||
            pending.OwnedUnitsBefore is < 0 or > AugNumbers.Validation.MaxOwnedSupplyUnits ||
            (pending.CreditsAfter != null &&
             pending.CreditsAfter != pending.CreditsBefore - pending.UnitPrice) ||
            (pending.OwnedUnitsAfter != null &&
             pending.OwnedUnitsAfter != pending.OwnedUnitsBefore + 1) ||
            (pending.CreditsAfter == null) != (pending.OwnedUnitsAfter == null) ||
            (pending.CreditsAfter == null) != (pending.EvidenceId == null) ||
            (pending.EvidenceId != null &&
             !SupplyDeliveryProofRules.IsEvidenceForRequest(pending.RequestId,
                 pending.EvidenceId)) ||
            pending.Phase is not (SupplyPurchaseRules.Prepared or
                SupplyPurchaseRules.Applying or SupplyPurchaseRules.ExternalConfirmed or
                SupplyPurchaseRules.NeedsRecovery) ||
            state.InitPhase != "complete" ||
            state.PendingOperation != null ||
            state.Cybernetics.PendingPurchase != null ||
            state.NeuralModules.PendingDelivery != null ||
            state.Appraisals.PendingScan != null ||
            (pending.Phase is SupplyPurchaseRules.Prepared or SupplyPurchaseRules.Applying &&
             (pending.CreditsAfter != null || pending.OwnedUnitsAfter != null ||
              pending.EvidenceId != null || pending.RecoveryReason != null ||
              pending.RecoveryObservation != null)) ||
            (pending.Phase == SupplyPurchaseRules.ExternalConfirmed &&
             (pending.CreditsAfter != pending.CreditsBefore - pending.UnitPrice ||
              pending.OwnedUnitsAfter != pending.OwnedUnitsBefore + 1 ||
              !SupplyDeliveryProofRules.IsEvidenceForRequest(pending.RequestId,
                  pending.EvidenceId) || pending.RecoveryReason != null ||
              pending.RecoveryObservation != null)) ||
            (pending.Phase == SupplyPurchaseRules.NeedsRecovery &&
             (string.IsNullOrWhiteSpace(pending.RecoveryReason) ||
              pending.RecoveryReason.Length > AugNumbers.Validation.MaxIdentifierChars ||
              pending.RecoveryObservation is { } observation &&
              (observation.CreditsAfter is < 0 or > int.MaxValue ||
               observation.OwnedUnitsAfter is < 0 or > AugNumbers.Validation.MaxOwnedSupplyUnits ||
               string.IsNullOrWhiteSpace(observation.ItemId) ||
               observation.ItemId.Length > AugNumbers.Validation.MaxIdentifierChars ||
               string.IsNullOrWhiteSpace(observation.EvidenceId) ||
               observation.EvidenceId.Length > AugNumbers.Validation.MaxIdentifierChars))))
            throw new InvalidOperationException("Invalid pending supply purchase");
        if (pending.Phase != SupplyPurchaseRules.NeedsRecovery &&
            (pending.Kind == SupplyPurchaseRules.Medical
                ? state.MedicalSupply is not { } pendingMedical ||
                  pendingMedical.PeriodDay != pending.PeriodKey ||
                  pendingMedical.ItemId != pending.ItemId ||
                  pendingMedical.Stock != pending.StockBefore ||
                  pendingMedical.UnitPrice != pending.UnitPrice
                : state.ElectronicSupply is not { } pendingElectronic ||
                  pendingElectronic.PeriodId != pending.PeriodKey ||
                  pending.ItemId != SupplyRules.ElectronicItemId ||
                  pendingElectronic.Stock != pending.StockBefore ||
                  pendingElectronic.UnitPrice != pending.UnitPrice))
            throw new InvalidOperationException("Pending supply offer changed");
    }

    private static void ValidateAppraisals(RunState state)
    {
        var appraisals = state.Appraisals;
        if (appraisals == null || appraisals.Submissions == null ||
            appraisals.Submissions.Length > AugNumbers.Validation.MaxAppraisalSubmissions ||
            appraisals.ScannedItems == null ||
            appraisals.ScannedItems.Length > AugNumbers.Validation.MaxScannedItems ||
            appraisals.ScanReceipts == null ||
            appraisals.ScanReceipts.Length > AugNumbers.Validation.MaxScanReceipts ||
            appraisals.ScanCache == null ||
            appraisals.ScanCache.Length > AugNumbers.Validation.MaxScanCacheEntries)
            throw new InvalidOperationException("Invalid appraisal collections");
        if (appraisals.DebugAllCategoriesUnlocked && state.InitPhase != "complete")
            throw new InvalidOperationException("Debug appraisal unlock before opening completion");
        int? eyePurchaseDay = state.Cybernetics.Purchases.FirstOrDefault(x =>
                x.ItemId == CyberneticRules.CyberEye)?.Day ??
            state.Cybernetics.NetworkUnlocks?.FirstOrDefault(x =>
                x.ItemId == CyberneticRules.CyberEye)?.Day;
        if ((appraisals.ScanReceipts.Length != 0 ||
             appraisals.PendingScan != null) && eyePurchaseDay == null)
            throw new InvalidOperationException("Eye scan without purchased eye");
        var submissionItems = new HashSet<string>(StringComparer.Ordinal);
        var submissionRequests = new HashSet<string>(StringComparer.Ordinal);
        var lastSubmissionDay = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var receipt in appraisals.Submissions)
        {
            if (receipt == null || !AppraisalRules.ValidText(receipt.ItemInstanceId) ||
                !AppraisalRules.IsCategory(receipt.CategoryId) ||
                !ValidGuid(receipt.RequestId) ||
                !submissionItems.Add(receipt.ItemInstanceId) ||
                !submissionRequests.Add(receipt.RequestId) ||
                receipt.Day < state.StartDay || receipt.Day > state.LastObservedDay ||
                receipt.VerificationKind !=
                    AppraisalRules.VerificationKind(receipt.CategoryId) ||
                !AppraisalRules.ValidText(receipt.EvidenceId) ||
                !AppraisalRules.ValidText(receipt.IdentityEvidenceId))
                throw new InvalidOperationException("Invalid appraisal submission");
            lastSubmissionDay[receipt.ItemInstanceId] = receipt.Day;
        }
        var scannedItems = new Dictionary<string, AppraisalScannedItem>(
            StringComparer.Ordinal);
        foreach (var item in appraisals.ScannedItems)
        {
            if (item == null || !AppraisalRules.ValidText(item.ItemInstanceId) ||
                !AppraisalRules.IsCategory(item.CategoryId) ||
                !ValidGuid(item.FirstScanRequestId) ||
                item.Day < state.StartDay || item.Day > state.LastObservedDay ||
                !AppraisalRules.ValidText(item.IdentityEvidenceId) ||
                !scannedItems.TryAdd(item.ItemInstanceId, item))
                throw new InvalidOperationException("Invalid scanned item marker");
        }
        var receiptIds = new HashSet<string>(StringComparer.Ordinal);
        var receipts = new HashSet<AppraisalScanReceipt>();
        var markersWithFirstReceipt = new HashSet<string>(StringComparer.Ordinal);
        var lastReceiptRequest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var receipt in appraisals.ScanReceipts)
        {
            if (receipt == null || !ValidGuid(receipt.RequestId) ||
                !receiptIds.Add(receipt.RequestId) ||
                !AppraisalRules.ValidText(receipt.ItemInstanceId) ||
                !scannedItems.TryGetValue(receipt.ItemInstanceId,
                    out var marker) || marker.CategoryId != receipt.CategoryId ||
                !AppraisalRules.ValidText(receipt.Fingerprint) ||
                !AppraisalRules.ValidText(receipt.AdapterVersion) ||
                !AppraisalRules.ValidText(receipt.BatteryInstanceId) ||
                !AppraisalRules.ValidText(receipt.BatteryEvidenceId) ||
                receipt.Day < marker.Day || receipt.Day > state.LastObservedDay ||
                receipt.Day < eyePurchaseDay!.Value ||
                receipt.ChargeBefore is < 0 or > AugNumbers.Validation.MaxBatteryCharge ||
                receipt.ChargeAfter < 0 ||
                receipt.ChargeAfter != receipt.ChargeBefore -
                    state.ConfigSnapshot.EyeScanBatteryCost)
                throw new InvalidOperationException("Invalid eye scan receipt");
            if (marker.FirstScanRequestId == receipt.RequestId &&
                marker.Day == receipt.Day)
                markersWithFirstReceipt.Add(receipt.ItemInstanceId);
            lastReceiptRequest[receipt.ItemInstanceId] = receipt.RequestId;
            receipts.Add(receipt);
        }
        foreach (var marker in appraisals.ScannedItems)
        {
            if (!markersWithFirstReceipt.Contains(marker.ItemInstanceId) ||
                lastSubmissionDay.TryGetValue(marker.ItemInstanceId, out int submittedDay) &&
                submittedDay > marker.Day)
                throw new InvalidOperationException("Scanned marker has no first receipt");
        }
        var cachedItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cache in appraisals.ScanCache)
        {
            if (cache == null || !cachedItems.Add(cache.ItemInstanceId) ||
                !AppraisalScanRules.ValidResult(cache.Result) ||
                !receipts.Contains(new AppraisalScanReceipt(cache.RequestId,
                    cache.ItemInstanceId, cache.CategoryId, cache.Fingerprint,
                    cache.AdapterVersion, cache.Day, cache.BatteryInstanceId,
                    cache.ChargeBefore, cache.ChargeAfter,
                    cache.BatteryEvidenceId)) ||
                !lastReceiptRequest.TryGetValue(cache.ItemInstanceId,
                    out var lastRequest) || lastRequest != cache.RequestId)
                throw new InvalidOperationException("Invalid eye scan cache");
        }
        if (appraisals.PendingScan is not { } pending) return;
        if (!ValidGuid(pending.RequestId) ||
            receiptIds.Contains(pending.RequestId) ||
            !AppraisalRules.ValidText(pending.ItemInstanceId) ||
            !AppraisalRules.IsCategory(pending.CategoryId) ||
            (scannedItems.TryGetValue(pending.ItemInstanceId, out var scanned) &&
             scanned.CategoryId != pending.CategoryId) ||
            !AppraisalRules.ValidText(pending.Fingerprint) ||
            !AppraisalRules.ValidText(pending.AdapterVersion) ||
            !AppraisalScanRules.ValidResult(pending.Result) ||
            pending.Day < state.StartDay || pending.Day > state.LastObservedDay ||
            pending.Day < eyePurchaseDay!.Value ||
            !AppraisalRules.ValidText(pending.IdentityEvidenceId) ||
            !AppraisalRules.ValidText(pending.EyeToolInstanceId) ||
            pending.EyeToolInstanceId != state.EyeToolInstanceId ||
            !AppraisalRules.ValidText(pending.EyeToolEvidenceId) ||
            !AppraisalRules.ValidText(pending.BatteryInstanceId) ||
            !AppraisalRules.ValidText(pending.BatteryOwnershipEvidenceId) ||
            pending.ChargeBefore is < 0 or > AugNumbers.Validation.MaxBatteryCharge ||
            pending.Cost != state.ConfigSnapshot.EyeScanBatteryCost ||
            pending.ChargeBefore < pending.Cost ||
            pending.Phase is not (AppraisalScanRules.Prepared or
                AppraisalScanRules.Applying or AppraisalScanRules.ExternalConfirmed or
                AppraisalScanRules.NeedsRecovery) ||
            (pending.Phase is AppraisalScanRules.Prepared or
                AppraisalScanRules.Applying &&
             (pending.Observation != null || pending.RecoveryReason != null)) ||
            (pending.Phase == AppraisalScanRules.ExternalConfirmed &&
             (pending.Observation == null || pending.RecoveryReason != null ||
              !ValidScanObservation(pending, pending.Observation))) ||
            (pending.Phase == AppraisalScanRules.NeedsRecovery &&
             ((pending.Observation != null &&
               !ValidScanObservationText(pending.Observation)) ||
              !AppraisalRules.ValidText(pending.RecoveryReason))) ||
            state.PendingOperation != null ||
            state.Cybernetics.PendingPurchase != null ||
            state.NeuralModules.PendingDelivery != null)
            throw new InvalidOperationException("Invalid pending eye scan");
    }

    private static bool ValidScanObservation(AppraisalScanPending pending,
        AppraisalBatteryObservation observation)
        => ValidScanObservationText(observation) &&
           observation.BatteryInstanceId == pending.BatteryInstanceId &&
           observation.ChargeBefore == pending.ChargeBefore &&
           observation.ChargeAfter == pending.ChargeBefore - pending.Cost;

    private static bool ValidScanObservationText(AppraisalBatteryObservation observation)
        => AppraisalRules.ValidText(observation.BatteryInstanceId) &&
           AppraisalRules.ValidText(observation.EvidenceId) &&
           observation.ChargeBefore is >= 0 and <= AugNumbers.Validation.MaxBatteryCharge &&
           observation.ChargeAfter is >= 0 and <= AugNumbers.Validation.MaxBatteryCharge;

    private static bool ValidGuid(string? value)
        => Guid.TryParse(value, out var parsed) && parsed != Guid.Empty;

    private static bool ValidMachineText(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= AugNumbers.Validation.MaxIdentifierChars;

    private static void ValidateMachines(RunState state)
    {
        var machine = state.Machines;
        var config = state.ConfigSnapshot;
        if (machine == null || machine.Connections == null ||
            machine.NextConnectionEpoch < 1 ||
            machine.Connections.Length > state.NeuralModules.Receipts.Length)
            throw new InvalidOperationException("Invalid machine state");
        var machineIds = new HashSet<string>(StringComparer.Ordinal);
        var moduleIds = new HashSet<string>(StringComparer.Ordinal);
        var epochs = new HashSet<long>();
        foreach (var connection in machine.Connections)
        {
            if (connection == null || !ValidMachineText(connection.ItemInstanceId) ||
                !ValidMachineText(connection.ModuleInstanceId) ||
                !machineIds.Add(connection.ItemInstanceId) ||
                !moduleIds.Add(connection.ModuleInstanceId) ||
                !state.NeuralModules.Receipts.Any(x =>
                    x.InstanceId == connection.ModuleInstanceId) ||
                !config.MachineProfiles.Any(profile => profile.ModelId == connection.ModelId) ||
                connection.Epoch < 1 || connection.Epoch >= machine.NextConnectionEpoch ||
                !epochs.Add(connection.Epoch) || connection.ConnectedDay < state.StartDay ||
                connection.ConnectedDay > state.LastObservedDay ||
                !ValidMachineText(connection.OwnershipEvidenceId))
                throw new InvalidOperationException("Invalid machine connection");
        }
    }

    private static void ValidateCybernetics(RunState state)
    {
        var cyber = state.Cybernetics;
        if (cyber == null || cyber.Purchases == null || cyber.EnabledIds == null ||
            cyber.Purchases.Length > 4 || cyber.EnabledIds.Length > 4)
            throw new InvalidOperationException("Invalid cybernetic collection");
        var networkUnlocks = cyber.NetworkUnlocks ?? [];
        if (networkUnlocks.Length > 13)
            throw new InvalidOperationException("Invalid network cybernetic collection");
        var items = new HashSet<string>(StringComparer.Ordinal);
        var requests = new HashSet<string>(StringComparer.Ordinal);
        int previousDay = state.StartDay;
        foreach (var receipt in cyber.Purchases)
        {
            if (receipt == null || !CyberneticRules.IsCatalogItem(receipt.ItemId) ||
                !items.Add(receipt.ItemId) || !ValidGuid(receipt.RequestId) ||
                !requests.Add(receipt.RequestId) ||
                receipt.Price != CyberneticRules.Price(state.ConfigSnapshot, receipt.ItemId) ||
                receipt.CreditsBefore is < 0 or > int.MaxValue ||
                receipt.CreditsAfter != receipt.CreditsBefore - receipt.Price ||
                receipt.Day < previousDay ||
                receipt.Day > state.LastObservedDay ||
                string.IsNullOrWhiteSpace(receipt.EvidenceId) || receipt.EvidenceId.Length > AugNumbers.Validation.MaxIdentifierChars)
                throw new InvalidOperationException("Invalid cybernetic purchase receipt");
            previousDay = receipt.Day;
        }
        int previousNetworkDay = state.StartDay;
        foreach (var receipt in networkUnlocks)
        {
            if (receipt == null || !CyberneticRules.IsCatalogItem(receipt.ItemId) ||
                receipt.UpgradeId != CyberneticRules.NetworkUpgradeId(receipt.ItemId) ||
                !items.Add(receipt.ItemId) ||
                receipt.Day < previousNetworkDay || receipt.Day > state.LastObservedDay)
                throw new InvalidOperationException("Invalid network cybernetic receipt");
            bool creditPurchase = receipt.CreditsCost is > 0;
            if (creditPurchase
                ? receipt.WildFavorCost != 0 || receipt.WildFavorAfter != 0 ||
                  receipt.CreditsCost != CyberneticRules.Price(state.ConfigSnapshot,
                      receipt.ItemId) || receipt.CreditsBefore is null or < 0 ||
                  receipt.CreditsAfter != receipt.CreditsBefore - receipt.CreditsCost ||
                  (receipt.Materials == null
                      ? !CyberneticRules.ValidLegacyMaterial(
                            state.ConfigSnapshot.CyberneticPrices, receipt.ItemId,
                            receipt.MaterialItemId) ||
                        receipt.MaterialUniqueId is null or <= 0 ||
                        receipt.MaterialUnitsBefore is null or <= 0
                      : receipt.MaterialItemId != null ||
                        receipt.MaterialUniqueId != null ||
                        receipt.MaterialUnitsBefore != null ||
                        !CyberneticRules.ValidMaterialReceipt(
                            state.ConfigSnapshot.CyberneticPrices,
                            receipt.ItemId, receipt.Materials))
                : receipt.WildFavorCost <= 0 ||
                  receipt.WildFavorCost != CyberneticRules.Price(state.ConfigSnapshot,
                      receipt.ItemId) || receipt.WildFavorAfter < 0 ||
                  receipt.WildFavorAfter > int.MaxValue - receipt.WildFavorCost ||
                  receipt.CreditsCost != null || receipt.CreditsBefore != null ||
                  receipt.CreditsAfter != null || receipt.MaterialItemId != null ||
                  receipt.MaterialUniqueId != null || receipt.MaterialUnitsBefore != null ||
                  receipt.Materials != null)
                throw new InvalidOperationException("Invalid network cybernetic payment receipt");
            previousNetworkDay = receipt.Day;
        }
        if (cyber.CyberEyeDelivery is { } delivery)
        {
            int? unlockedDay = cyber.Purchases.FirstOrDefault(x =>
                x.ItemId == CyberneticRules.CyberEye)?.Day ??
                networkUnlocks.FirstOrDefault(x =>
                    x.ItemId == CyberneticRules.CyberEye)?.Day;
            if (unlockedDay == null || delivery.ItemId != CyberneticRules.CyberEye ||
                delivery.NativeUniqueId <= 0 || delivery.Day < unlockedDay ||
                delivery.Day > state.LastObservedDay ||
                delivery.EvidenceId !=
                    $"native-item:{delivery.NativeUniqueId}:owned")
                throw new InvalidOperationException("Invalid cyber eye delivery receipt");
        }
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        if (cyber.EnabledIds.Any(id => !items.Contains(id) || !enabled.Add(id)))
            throw new InvalidOperationException("Invalid enabled cybernetic");
        if (cyber.PendingPurchase is not { } pending) return;
        if (!CyberneticRules.IsCatalogItem(pending.ItemId) || items.Contains(pending.ItemId) ||
            !ValidGuid(pending.RequestId) || requests.Contains(pending.RequestId) ||
            pending.Price != CyberneticRules.Price(state.ConfigSnapshot, pending.ItemId) ||
            pending.CreditsBefore is < 0 or > int.MaxValue ||
            pending.CreditsBefore < pending.Price ||
            pending.Day < state.StartDay ||
            pending.Day > state.LastObservedDay ||
            state.PendingOperation != null ||
            pending.Phase is not (CyberneticRules.Prepared or CyberneticRules.Applying or
                                  CyberneticRules.ExternalConfirmed or CyberneticRules.NeedsRecovery) ||
            (pending.Phase is CyberneticRules.Prepared or CyberneticRules.Applying &&
             (pending.CreditsAfter != null || pending.EvidenceId != null ||
              pending.RecoveryReason != null || pending.RecoveryObservation != null)) ||
            (pending.Phase == CyberneticRules.ExternalConfirmed &&
             (pending.CreditsAfter != pending.CreditsBefore - pending.Price ||
              string.IsNullOrWhiteSpace(pending.EvidenceId) || pending.EvidenceId.Length > AugNumbers.Validation.MaxIdentifierChars ||
              pending.RecoveryReason != null || pending.RecoveryObservation != null)) ||
            (pending.Phase == CyberneticRules.NeedsRecovery &&
             (string.IsNullOrWhiteSpace(pending.RecoveryReason) ||
              pending.RecoveryReason.Length > AugNumbers.Validation.MaxIdentifierChars ||
              (pending.CreditsAfter != null && pending.CreditsAfter is < 0 or > int.MaxValue) ||
              (pending.EvidenceId != null &&
               (string.IsNullOrWhiteSpace(pending.EvidenceId) || pending.EvidenceId.Length > AugNumbers.Validation.MaxIdentifierChars)) ||
              (pending.RecoveryObservation != null &&
               (pending.RecoveryObservation.CreditsAfter is < 0 or > int.MaxValue ||
                string.IsNullOrWhiteSpace(pending.RecoveryObservation.EvidenceId) ||
                pending.RecoveryObservation.EvidenceId.Length > AugNumbers.Validation.MaxIdentifierChars)))))
            throw new InvalidOperationException("Invalid pending cybernetic purchase");
    }

    internal static void ValidateConfig(ConfigSnapshot config)
    {
        if (config.InitialCredits < 0 || config.InitialRent is < 0 or > AugNumbers.Validation.MaxRent ||
            config.InitialSecurityReputation is < -AugNumbers.Validation.MaxSecurityReputationMagnitude or > AugNumbers.Validation.MaxSecurityReputationMagnitude ||
            config.InitialBatteryCount is < 1 or > AugNumbers.Validation.MaxGrantUnits ||
            config.InitialMedicalCount is < 1 or > AugNumbers.Validation.MaxGrantUnits ||
            config.InitialElectronicCount is < 1 or > AugNumbers.Validation.MaxGrantUnits ||
            config.MaxHealth is < 1 or > AugNumbers.Validation.MaxHealth || config.InitialHealth < 1 || config.InitialHealth > config.MaxHealth ||
            config.DailyHealthLoss < 0 || config.DailyHealthLoss > config.MaxHealth ||
            config.MaxExposure is < 1 or > AugNumbers.Validation.MaxExposure || config.InitialExposure < 0 || config.InitialExposure >= config.MaxExposure ||
            config.DailyExposureGain < 0 || config.DailyExposureGain > config.MaxExposure ||
            config.ExposureGraceDays is < 0 or > AugNumbers.Validation.MaxExposureGraceDays ||
            config.BargainThreshold1 < 1 || config.BargainThreshold1 >= config.BargainThreshold2 ||
            config.BargainThreshold2 >= config.BargainThreshold3 || config.BargainThreshold3 > config.MaxExposure ||
            config.BargainPenalty1Bp is < 0 or > 10_000 ||
            config.BargainPenalty2Bp < config.BargainPenalty1Bp || config.BargainPenalty2Bp > 10_000 ||
            config.BargainPenalty3Bp < config.BargainPenalty2Bp || config.BargainPenalty3Bp > 10_000 ||
            config.SameKindBonusBp is < 0 or > 10_000 || config.VoiceBonusBp is < 0 or > 10_000 ||
            config.MedicalRecoveryBudget < config.DailyHealthLoss || config.MedicalRecoveryBudget is < 1 or > AugNumbers.Validation.MaxRecoveryBudget ||
            config.ElectronicSupplyTargetStock is < 1 or > AugNumbers.Validation.MaxSupplyStock ||
            config.ElectronicSupplyCycleDays is < 1 or > AugNumbers.Validation.MaxSupplyCycleDays ||
            config.SupplyPriceMultiplierBp is < 1 or > AugNumbers.Validation.MaxSupplyPriceMultiplierBp ||
            config.ExposureRepairPerElectronic < 1 || config.ExposureRepairPerElectronic > config.MaxExposure ||
            config.NeuralModuleTestPrice is < 1 or > AugNumbers.Validation.MaxNeuralTestPrice ||
            config.AppraisalPerfectRequired is < 1 or > AugNumbers.Validation.MaxPerfectAppraisalsRequired ||
            config.EyeScanBatteryCost is < 1 or > AugNumbers.Validation.MaxEyeScanBatteryCost ||
            config.MachineProfiles == null || config.MachineProfiles.Length > AugNumbers.Validation.MaxMachineProfiles ||
            config.MachineProfiles.Any(profile => profile == null ||
                string.IsNullOrWhiteSpace(profile.ModelId) || profile.ModelId.Length > AugNumbers.Validation.MaxIdentifierChars ||
                string.IsNullOrWhiteSpace(profile.AdapterEvidenceId) ||
                profile.AdapterEvidenceId.Length > AugNumbers.Validation.MaxIdentifierChars) ||
            config.MachineProfiles.Select(profile => profile.ModelId)
                .Distinct(StringComparer.Ordinal).Count() != config.MachineProfiles.Length ||
            config.CyberneticPrices == null)
            throw new InvalidOperationException("Invalid configuration snapshot");
        var cyber = config.CyberneticPrices;
        long[] prices = [cyber.SleepControl, cyber.ArtificialAnkle,
            cyber.InfraredVision, cyber.CyberEye, cyber.CommunicationReceiver,
            cyber.KineticRecovery, cyber.TitaniumSkeleton, cyber.FineMotorAssistant,
            cyber.SyntheticVocalCords, cyber.ArtificialHeart,
            cyber.MimeticSkin, cyber.AiAssistantCore];
        if (prices.Any(price => price is < 1 or > int.MaxValue) ||
            cyber.UnlockMaterialItemIds == null ||
            cyber.UnlockMaterialItemIds.Length is < 1 or > AugNumbers.Validation.MaxUnlockMaterialIds ||
            cyber.UnlockMaterialItemIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > AugNumbers.Validation.MaxIdentifierChars) ||
            cyber.UnlockMaterialItemIds.Distinct(StringComparer.Ordinal).Count() !=
                cyber.UnlockMaterialItemIds.Length ||
            cyber.UnlockMaterialCount != 1 ||
            cyber.SleepExtraNightActions is < 0 or > AugNumbers.Validation.MaxExtraActions ||
            cyber.AnkleExtraScavAttempts is < 0 or > AugNumbers.Validation.MaxExtraActions ||
            cyber.InfraredWoundChanceMultiplierBp is < 0 or > 10_000 ||
            cyber.MachineEfficiencyBonusBp is < 0 or > AugNumbers.Validation.MaxCyberneticMachineBonusBp ||
            cyber.MachinePerformanceBonusBp is < 0 or > AugNumbers.Validation.MaxCyberneticMachineBonusBp ||
            cyber.MachineQualityBonusBp is < 0 or > AugNumbers.Validation.MaxCyberneticMachineBonusBp ||
            cyber.ExposureThresholdShift is < 0 or > AugNumbers.Validation.MaxExposureThresholdShift ||
            cyber.ArtificialHeartHealthLossMultiplierBp is < 0 or > 10_000 ||
            config.CyberneticPrices.SkinDailyExposureReduction is < 0 or > AugNumbers.Validation.MaxSkinExposureReduction)
            throw new InvalidOperationException("Invalid cybernetic configuration");
        if (cyber.UnlockMaterials is { } materials &&
            (typeof(CyberneticMaterialConfig).GetProperties().Any(property =>
                property.GetValue(materials) is not string id ||
                string.IsNullOrWhiteSpace(id) || id.Length > AugNumbers.Validation.MaxIdentifierChars)))
            throw new InvalidOperationException("Invalid cybernetic material configuration");
        if (cyber.UnlockRecipes is { } recipes &&
            (recipes.Count > AugNumbers.Validation.MaxUnlockRecipes || recipes.Any(entry =>
                !CyberneticRules.IsRecipeKey(entry.Key) ||
                entry.Value is not { Length: > 0 and <= AugNumbers.Validation.MaxRecipeIngredients } ||
                entry.Value.Any(cost => cost == null ||
                    string.IsNullOrWhiteSpace(cost.ItemId) ||
                    cost.ItemId.Length > AugNumbers.Validation.MaxIdentifierChars || cost.Quantity is < 1 or > AugNumbers.Validation.MaxIngredientUnits) ||
                entry.Value.Sum(cost => cost.Quantity) > AugNumbers.Validation.MaxRecipeMaterialUnits ||
                entry.Value.Select(cost => cost.ItemId).Distinct(StringComparer.Ordinal)
                    .Count() != entry.Value.Length)))
            throw new InvalidOperationException("Invalid cybernetic unlock recipe");
    }

    private static string ConfigHash(ConfigSnapshot config)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config, JsonOptions));
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
    }
}
