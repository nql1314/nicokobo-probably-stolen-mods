namespace Nicokobo.ContentInterop;

public static class FixedModuleAbilities
{
    public static void RequireVersion(int version)
    {
        int available;
        try { available = ForgeClient.Call<int>("module.abilities.version"); }
        catch (ArgumentException error)
        { throw new InvalidOperationException("This content requires the fixed-module ability API in NicoKobo Forge. Please update or reinstall Forge.", error); }
        if (available != version) throw new InvalidOperationException("Incompatible Forge fixed-module ability API: " + available);
    }
    public static void Register(string owner, string id, Func<GameItem, bool> appliesTo,
        Func<string> name, Func<string> description, Action<GameItem, GameInventory?>? compute = null) =>
        ForgeClient.Call("module.abilities.register", owner, id, appliesTo, name, description, compute!);
    public static bool Contains(string id) => ForgeClient.Call<bool>("module.abilities.contains", id);
    public static void Add(GameItem item, string id, int parameter = 0) => ForgeClient.Call("module.abilities.add", item, id, parameter);
    public static void Remove(string owner) => ForgeClient.Call("module.abilities.remove", owner);
}
