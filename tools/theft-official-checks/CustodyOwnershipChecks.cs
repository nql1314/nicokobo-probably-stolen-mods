using System.Text.Json;
using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;

internal static class CustodyOwnershipChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var night = new NightState { Started = true };
        night.Loot.Add(new ItemState { UniqueId = 501, Graph = JsonSerializer.Serialize(new
        {
            saveItems = new[]
            {
                new { identifier = "water_tablet_container", uniqueId = 501, unitCount = 1 },
                new { identifier = "water_tablet", uniqueId = 502, unitCount = 5 }
            }
        }) });
        var ownedIds = CustodyRules.OwnedIds(night);
        check(ownedIds.SetEquals(new[] { 501, 502 }),
            "Official custody includes the current loot root and every saved nested UID");
        check(ReferenceEquals(ownedIds, CustodyRules.OwnedIds(night)),
            "Repeated inventory queries reuse the loot ID index for the same committed night");
        check(!CustodyRules.CanMove(night, new HashSet<int> { 502 }, Array.Empty<int>(), true, true),
            "Official custody prevents extracting a stolen nested consumable");
        check(CustodyRules.CanMove(night, new HashSet<int> { 501, 502 }, new[] { 600 }, true, true),
            "The complete stolen container can move to a personal carry bag");
        check(!CustodyRules.CanMove(night, new HashSet<int> { 501, 502 }, Array.Empty<int>(), false, true),
            "A stolen container cannot leave native carry before return");
        check(!CustodyRules.CanMove(night, new HashSet<int> { 501, 502 }, Array.Empty<int>(), true, false),
            "A stolen container cannot stack into an occupied carry destination");
        check(!CustodyRules.CanMove(night, new HashSet<int> { 700 }, new[] { 501 }, true, true),
            "A personal item cannot be inserted into the protected stolen container");
        check(CustodyRules.CanMove(night, new HashSet<int> { 700 }, Array.Empty<int>(), false, false),
            "Unrelated personal items remain movable while loot is protected");
        check(CustodyRules.OwnedIds(new NightState { Started = true }).Count == 0,
            "Committing a night after abandoning the container releases its root and contents");
        night.Started = false;
        check(CustodyRules.OwnedIds(night).Count == 0, "Browsing before theft has no custody lock");
        night.Started = true; night.Ended = true;
        check(CustodyRules.OwnedIds(night).Count == 0, "A completed outing releases custody");
        check(CustodyRules.OwnedIds(null).Count == 0, "No night creates no custody lock");
    }
}
