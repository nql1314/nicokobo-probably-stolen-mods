using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.ModuleMatrix.Definitions;

internal static class PriceRollbackChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var definition in ModuleTierDefinitions.All.Where(item => !item.IsMatrix))
        {
            var item = new GameItem { identifier = definition.Id, unitValue = 1234, unitBaseValue = 87 };
            item.ModifyTag("checks.other_price_source", tag => tag.Enable().SetInt(200));
            MatrixItems.Reprice(item); MatrixItems.Reprice(item);
            check(item.unitValue == 1234 && item.unitBaseValue == 87 && !item.IsTag(MatrixItems.PriceTag),
                "Untagged native price was changed or claimed by Matrix: " + definition.Id);
            check(item.GetTagReadonly("checks.other_price_source").GetInt() == 200,
                "Matrix changed another price source: " + definition.Id);
        }

        foreach (var (id, original, applied) in new[]
        {
            ("system_module_performance", 70, 70), ("system_module_efficiancy", 70, 70),
            ("system_module_quality", 70, 70), ("node_small", 50, 60), ("node_medium", 50, 70),
            ("system_module_overclock", 100, 130), ("system_module_eco", 100, 130),
            ("system_module_fineness", 100, 130), ("system_module_streamlining", 125, 130)
        })
        {
            // The premium and a later external value change both survive removing Matrix's difference.
            var item = new GameItem { identifier = id, unitValue = applied + 215, unitBaseValue = 87 };
            item.ModifyTag(MatrixItems.PriceTag, tag => tag.Enable().SetInt(applied));
            MatrixItems.Reprice(item);
            check(item.unitValue == original + 215 && item.unitBaseValue == 87 && !item.IsTag(MatrixItems.PriceTag),
                "Tagged native price did not restore only Matrix's difference: " + id);
            MatrixItems.Reprice(item);
            item.state = Newtonsoft.Json.JsonConvert.DeserializeObject<TagSystem>(
                Newtonsoft.Json.JsonConvert.SerializeObject(item.state))!;
            MatrixItems.Reprice(item);
            check(item.unitValue == original + 215 && !item.IsTag(MatrixItems.PriceTag),
                "Native price rollback repeated after another access or reload: " + id);
        }

        var matrix = new GameItem { identifier = FixedModuleDefinitions.M03.Id, unitValue = 147, unitBaseValue = 11 };
        MatrixItems.Reprice(matrix); MatrixItems.Reprice(matrix);
        check(matrix.unitValue == 87 && matrix.unitBaseValue == 11 &&
            matrix.GetTagReadonly(MatrixItems.PriceTag).GetInt() == 70,
            "Matrix's independent price migration discarded extra value or compounded");
        check(ModuleTierDefinitions.RarePool(_ => true).Count == 13,
            "Returning native prices changed the rare upgrade pool");
    }
}
