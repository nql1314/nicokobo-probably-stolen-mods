namespace AugMechanicalAscension.Official;
internal static class OfficialProductionValueApi
{
    public const string BasisTag = Nicokobo.ContentInterop.ProductionValue.BasisTag;
    internal static void SetBasis(GameItem item, long basis) => Nicokobo.ContentInterop.ProductionValue.SetBasis(item, basis);
    internal static long ReadBasis(GameItem item, long fallback) => Nicokobo.ContentInterop.ProductionValue.ReadBasis(item, fallback);
}
