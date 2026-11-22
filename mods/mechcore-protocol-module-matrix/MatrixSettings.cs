namespace MechcoreProtocol.ModuleMatrix;

internal sealed record MatrixSettings(bool Fixed, bool Placement, bool Continuous, bool Nodes, bool Machines)
{
    internal static MatrixSettings Load(string packageId = MechcorePackage.Id)
    {
        var config = ModHelper.GetConfig(packageId);
        bool Read(string key, string label) =>
            config.Define(key, MechcorePackage.PreviousBoolean(MatrixMod.Id, key, true), "matrix.config.content", () => MatrixText.Get(label),
                () => MatrixText.Get("matrix.restart_the_game_after_changing_this_setting"), requiresRestart: true).Value;
        return new(Read("EnableP1FixedModules", "matrix.setting.enablep1fixedmodules"),
            Read("EnableP2PlacementSlice", "matrix.setting.enablep2placementslice"),
            Read("EnableP4ContinuousModules", "matrix.setting.enablep4continuousmodules"),
            Read("EnableNodeItems", "matrix.setting.enablenodeitems"),
            Read("EnableMachineModules", "matrix.setting.enablemachinemodules"));
    }
}
