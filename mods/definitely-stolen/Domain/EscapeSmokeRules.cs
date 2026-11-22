namespace DefinitelyStolen.Domain;

internal static class EscapeSmokeRules
{
    internal const string ItemId = Identity.Owner + ".escape_smoke";
    internal const string NativeItemId = "smoke_grenade";
    internal const double StockChance = .05;
    internal const int Value = 120;

    internal static bool IsEscapeSmoke(string itemId) => itemId is NativeItemId or ItemId;
}
