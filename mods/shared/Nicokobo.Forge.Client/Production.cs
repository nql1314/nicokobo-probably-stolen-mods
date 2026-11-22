namespace Nicokobo.ContentInterop;

public static class ProductionBatch
{
    public static List<BatchInput>? SelectInputs(GameGridInventory inventory, IEnumerable<IngredientView> rules) =>
        ForgeClient.Call("batch.select", inventory, rules.Select(ForgeWire.Ingredient).ToArray()) is object[][] values
            ? values.Select(v => new BatchInput((GameItem)v[0], (int)v[1], (int)v[2])).ToList() : null;
    public static bool Run(GameItem battery, GameGridInventory input, GameGridInventory output,
        IReadOnlyList<BatchInput> takes, int outputCount, Func<string> outputId, Action<GameItem, int> prepare,
        int power, GameItem? water = null, int waterParts = 0) => ForgeClient.Call<bool>("batch.run", battery, input, output,
            takes.Select(take => new object[] { take.Item, take.Count, take.Before }).ToArray(), outputCount, outputId, prepare, power, water!, waterParts);
}
public static class ProductionValue
{
    public const string BasisTag = "NICOKOBO_FORGE_PRODUCTION_BASIS_V1";
    public static decimal InputValue(GameItem item) => ForgeClient.Call<decimal>("value.input", item);
    public static decimal InnateFactor(GameItem item) => ForgeClient.Call<decimal>("value.innate", item);
    public static long ComposeRealValue(long basis, long innate, double final) => ForgeClient.Call<long>("value.compose", basis, innate, final);
    public static void SetBasis(GameItem item, long basis) => ForgeClient.Call("value.set", item, basis);
    public static long ReadBasis(GameItem item, long fallback) => ForgeClient.Call<long>("value.read", item, fallback);
    public static void RestoreBasis(GameItem item) => ForgeClient.Call("value.restore", item);
}
