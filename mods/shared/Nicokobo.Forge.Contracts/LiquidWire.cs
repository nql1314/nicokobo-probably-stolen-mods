namespace Nicokobo.ContentInterop;
internal static partial class ForgeWire
{
    internal static object[] Liquid(NativeMachineLiquidSnapshot value) => new object[] { value.CapacityParts,
        value.Contents.Select(p => new object[] { p.LiquidId, p.Parts, p.Value!, p.QualityBasis! }).ToArray() };
    internal static NativeMachineLiquidSnapshot Liquid(object[] value) => new((int)value[0],
        ((object[][])value[1]).Select(p => new NativeMachineLiquidPart((string)p[0], (int)p[1]) { Value = (decimal?)p[2], QualityBasis = (decimal?)p[3] }).ToArray());
}
