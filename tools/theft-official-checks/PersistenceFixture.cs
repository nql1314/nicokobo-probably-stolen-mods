// Minimal in-memory stand-ins for the official run-data surface. These checks
// exercise the production codec and staging adapter without loading Unity.
public sealed class PlayerStore
{
    public static PlayerStore Instance = new();
    public string runID = "";
    public int saveSlotId;
    public string startScenarioId = "";
    public NewGameData.StartType startType;
    public List<StartingPerk> startingPerks = new();
    public readonly Dictionary<string, string> modData = new(StringComparer.Ordinal);
    public Dictionary<string, string> ModData() => modData;
}
public sealed class NewGameData
{
    public static NewGameData? Instance;
    public int saveSlot;
    public string startScenarioId = "";
    public StartType startType;
    public List<StartingPerk> startingPerks = new();
    public enum StartType { APPRENTICE, MODDED }
}
public static class ModHelper
{
    public static string GetLocalized(string modId, string key, params object[] args) => key;
    public static void SetModData(string owner, string key, string value) => PlayerStore.Instance.modData[owner + ":" + key] = value;
    public static string? GetModData(string owner, string key) => PlayerStore.Instance.modData.TryGetValue(owner + ":" + key, out var value) ? value : null;
    public static void AddStartingPerk(string owner, string id, int cost, StartingPerk.StartingPerkType type, Func<string> name, Func<string> description, string? iconKey = null)
    { StartingPerkList.Perks.Add(new StartingPerk { id = id, cost = cost, type = type, nameProvider = name, descriptionProvider = description, iconKey = iconKey, source = owner }); }
}
public sealed class StartingPerk
{
    public string id = "", source = "";
    public int cost, maxSlot;
    public StartingPerkType type;
    public Func<string>? nameProvider, descriptionProvider;
    public string? iconKey;
    public enum StartingPerkType { POSITIVE }
    public static StartingPerk? GetPerkById(string id) => PlayerStore.Instance.startingPerks.SingleOrDefault(x => x.id == id);
}
public static class StartingPerkList { public static List<StartingPerk> Perks = new(); }
public static class StartingPerkIconLoader { public static Dictionary<string, object> perkIcons = new(); }
namespace DefinitelyStolen.Integration
{
    internal static class Native
    {
        internal static string Text(string chinese, string english) => english;
        internal static string Localize(string key, params object[] args) => Domain.CatalogText.GetFor(key, null, args);
        internal static bool IsCareer(PlayerStore store) => store.startScenarioId == DefinitelyStolen.Domain.Identity.StartId || (int)store.startType == DefinitelyStolen.Domain.Identity.NativeStartType;
    }
}
namespace DefinitelyStolen.UI
{
    internal static class Art { internal static object? TryGet(string name) => null; }
}
