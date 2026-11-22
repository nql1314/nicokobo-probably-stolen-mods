using System.Text;
using AugMechanicalAscension.Serialization;
using AugMechanicalAscension.Domain;
namespace AugMechanicalAscension.Persistence;

internal enum SlotReadbackStatus
{
    StructureReadable, StateMissing, StateMatched, StateMismatch,
    IdentityMismatch, NativeDayMismatch, NativeEconomyMismatch,
    NativeReputationMismatch, NativeInventoryMismatch, NativeStartTypeMismatch,
    NativeModDataMismatch,
    Malformed, ChangedDuringRead,
    TooLarge, Missing, Unavailable
}

internal sealed record SlotReadbackResult(SlotReadbackStatus Status, int ByteCount = 0);
internal enum NativeItemSearchScope { MainInventory, AllSavedInventories }
internal sealed record NativeItemIdentitySnapshot(string Identifier, int UniqueId,
    bool MustBePresent, int? UnitCount = null,
    NativeItemSearchScope SearchScope = NativeItemSearchScope.MainInventory,
    int? FullEnergy = null, bool RequirePlayerOwned = false);
internal sealed record NativeSaveSnapshot(int? Day = null, int? Cash = null,
    int? Rent = null, int? SecurityReputation = null,
    IReadOnlyDictionary<string, int>? MainRootUnitCounts = null,
    int? StartType = null, NativeItemIdentitySnapshot? OwnedItem = null,
    IReadOnlyDictionary<string, string>? ExtraModData = null,
    IReadOnlyList<NativeItemIdentitySnapshot>? OwnedItems = null);

