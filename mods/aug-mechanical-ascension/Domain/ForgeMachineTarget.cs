namespace AugMechanicalAscension.Domain;
internal static class OfficialMachineTarget
{
    internal const string OwnerId = "nicokobo.forge.machines";
    internal const string MachineId = "nicokobo.forge.machines.universal_manufacturing_terminal";
    internal static bool IsOwnedByForge => MachineId.StartsWith(OwnerId + ".", StringComparison.Ordinal);
}
