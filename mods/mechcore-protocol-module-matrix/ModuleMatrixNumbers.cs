namespace MechcoreProtocol.ModuleMatrix;

// 编译内置数值：修改本源码后，重新编译所属 Mod 才会生效。
// 本表不创建运行时配置；价格用信用点，尺寸用格数，Percent 用百分比，Bp 用基点（10000 = 100%）。
internal static class ModuleMatrixNumbers
{
    internal static class Distribution
    {
        // 模组表内的相对权重；不是固定掉落概率。
        internal const float FixedModuleLootWeight = 0.08f;
        // 每次夜间商店生成库存时，额外上架的不同 Mod 模组／节点种类数。
        internal const int NightShopDistinctItems = 2;
        internal const int NightShopBasicWeight = 2;
        // 功能性模组也参与稀有侧抽取，仍保留自身的功能性分类。
        internal const int NightShopRareWeight = 1;
    }

    /// <summary>固定模组的基础价格、属性和占格。</summary>
    /// <param name="UnitValue">物品基础单价（信用点）。</param>
    /// <param name="Performance">基础性能修正（整数百分点）；负数表示降低性能。</param>
    /// <param name="Efficiency">基础效率修正（整数百分点）；负数表示降低效率。</param>
    /// <param name="Quality">基础质量修正（整数百分点）；负数表示降低质量。</param>
    /// <param name="Width">物品外接矩形的宽度（格），须与原生 ShapeData 匹配。</param>
    /// <param name="Height">物品外接矩形的高度（格），须与原生 ShapeData 匹配。</param>
    internal readonly record struct ModuleNumbers(int UnitValue, int Performance, int Efficiency, int Quality, int Width, int Height);
    /// <summary>连续运行模组的基础数值和额外成长规则。</summary>
    /// <param name="UnitValue">物品基础单价（信用点）。</param>
    /// <param name="Performance">基础性能修正（整数百分点）；负数表示降低性能。</param>
    /// <param name="Efficiency">基础效率修正（整数百分点）；负数表示降低效率。</param>
    /// <param name="Quality">基础质量修正（整数百分点）；负数表示降低质量。</param>
    /// <param name="Width">物品外接矩形的宽度（格），须与原生 ShapeData 匹配。</param>
    /// <param name="Height">物品外接矩形的高度（格），须与原生 ShapeData 匹配。</param>
    /// <param name="Step">每次有效工作增加的对应属性百分点；属于额外成长量。</param>
    /// <param name="Cap">对应属性的总上限（百分点），包含基础属性；可成长量为 Cap 减基础属性。</param>
    internal readonly record struct ContinuousModuleNumbers(int UnitValue, int Performance, int Efficiency, int Quality, int Width, int Height, int Step, int Cap);
    /// <summary>节点物品的基础价格和占格。</summary>
    /// <param name="UnitValue">物品基础单价（信用点）。</param>
    /// <param name="Width">物品外接矩形的宽度（格），须与原生 ShapeData 匹配。</param>
    /// <param name="Height">物品外接矩形的高度（格），须与原生 ShapeData 匹配。</param>
    internal readonly record struct NodeNumbers(int UnitValue, int Width, int Height);
    /// <summary>机器专用模组的基础价格和占格。</summary>
    /// <param name="UnitValue">物品基础单价（信用点）。</param>
    /// <param name="Width">物品外接矩形的宽度（格），须与原生 ShapeData 匹配。</param>
    /// <param name="Height">物品外接矩形的高度（格），须与原生 ShapeData 匹配。</param>
    internal readonly record struct MachineModuleNumbers(int UnitValue, int Width, int Height);

    // 固定模组；属性正负值及占格的含义见 ModuleNumbers。
    internal static class FixedModules
    {
        // M01 平衡模组的基础数值。
        internal static readonly ModuleNumbers M01 =
            new(UnitValue: 70, Performance: 8, Efficiency: 8, Quality: 4, Width: 2, Height: 2);
        // M02 流水线模组的基础数值。
        internal static readonly ModuleNumbers M02 =
            new(UnitValue: 70, Performance: 12, Efficiency: 16, Quality: -4, Width: 1, Height: 4);
        // M03 精工模组的基础数值。
        internal static readonly ModuleNumbers M03 =
            new(UnitValue: 70, Performance: 8, Efficiency: -12, Quality: 12, Width: 3, Height: 2);
        // M04 稳态模组的基础数值。
        internal static readonly ModuleNumbers M04 =
            new(UnitValue: 70, Performance: -8, Efficiency: 20, Quality: 4, Width: 3, Height: 2);
        // M05 高功率模组的基础数值。
        internal static readonly ModuleNumbers M05 =
            new(UnitValue: 200, Performance: 60, Efficiency: -24, Quality: -8, Width: 2, Height: 3);
        // M06 检测模组的基础数值。
        internal static readonly ModuleNumbers M06 =
            new(UnitValue: 70, Performance: 4, Efficiency: -8, Quality: 6, Width: 1, Height: 2);
    }

    // 连续运行模组；成长步长和上限的含义见 ContinuousModuleNumbers。
    internal static class ContinuousModules
    {
        // M08 连续加速模组；Step 和 Cap 控制额外性能成长。
        internal static readonly ContinuousModuleNumbers M08 =
            new(UnitValue: 130, Performance: 8, Efficiency: 0, Quality: 0, Width: 1, Height: 4, Step: 4, Cap: 32);
        // M10 连续节能模组；Step 和 Cap 控制额外效率成长。
        internal static readonly ContinuousModuleNumbers M10 =
            new(UnitValue: 130, Performance: 0, Efficiency: 8, Quality: 0, Width: 2, Height: 2, Step: 4, Cap: 32);
        // M11 连续校准模组；Step 和 Cap 控制额外质量成长。
        internal static readonly ContinuousModuleNumbers M11 =
            new(UnitValue: 150, Performance: 0, Efficiency: 0, Quality: 4, Width: 3, Height: 2, Step: 2, Cap: 16);
    }

    internal static class Nodes
    {
        // N09 性能转换节点的基础价格和占格。
        internal static readonly NodeNumbers N09 =
            new(UnitValue: 130, Width: 1, Height: 1);
        // N10 节能转换节点的基础价格和占格。
        internal static readonly NodeNumbers N10 =
            new(UnitValue: 130, Width: 1, Height: 1);
        // N11 质量转换节点的基础价格和占格。
        internal static readonly NodeNumbers N11 =
            new(UnitValue: 130, Width: 1, Height: 1);
        // N12 隔离节点的基础价格和占格。
        internal static readonly NodeNumbers N12 =
            new(UnitValue: 130, Width: 1, Height: 2);
        // N16 学习加速节点的基础价格和占格。
        internal static readonly NodeNumbers N16 =
            new(UnitValue: 160, Width: 1, Height: 2);
    }

    internal static class MachineModules
    {
        // S24 渗透模组的基础价格和占格。
        internal static readonly MachineModuleNumbers S24 =
            new(UnitValue: 220, Width: 2, Height: 2);
        // S06 自动供水模组的基础价格和占格。
        internal static readonly MachineModuleNumbers S06 =
            new(UnitValue: 160, Width: 1, Height: 1);
        // S25 市场追踪模组的基础价格和占格。
        internal static readonly MachineModuleNumbers S25 =
            new(UnitValue: 200, Width: 2, Height: 2);
        // S09 解码缓存模组的基础价格和占格。
        internal static readonly MachineModuleNumbers S09 =
            new(UnitValue: 220, Width: 2, Height: 2);
        // S26 批量烧制模组：熔炉专用，1x4，售价 110。
        internal static readonly MachineModuleNumbers S26 =
            new(UnitValue: 110, Width: 1, Height: 4);
        // S11 定向投影模组的预留数值；当前功能隐藏。
        internal static readonly MachineModuleNumbers S11 =
            new(UnitValue: 240, Width: 2, Height: 2);
    }

    // 位置效果对自身正向基础属性的百分比加成；不是基点。
    internal static class Placement
    {
        // 角落挂载：同时接触两条相交边缘时的属性加成（%）。
        internal const int E01Percent = 30;
        // 留空增强：每接触一格不同空位时的属性加成（%）。
        internal const int E03Percent = 4;
        // 紧邻增强：每接触一件不同相邻模组时的属性加成（%）。
        internal const int E04Percent = 10;
        // 独立安装：不与模组或节点相邻时的属性加成（%）。
        internal const int E06Percent = 50;
        // 底部挂载：接触模组舱底部边缘时的属性加成（%）。
        internal const int E07Percent = 20;
        // 右侧挂载：接触模组舱右侧边缘时的属性加成（%）。
        internal const int E08Percent = 20;
    }

    internal static class Effects
    {
        // 转换逐个来源除法后向零截断再累加，负数也遵循同一规则。
        // 性能／效率转换到另一属性时的除数；默认保留原属性的 1/2。
        internal const double ConversionSecondaryDivisor = 2.0;
        // 性能／效率转换到质量时的除数；默认保留原属性的 1/4。
        internal const double ConversionQualityDivisor = 4.0;
        // 隔离节点保留正属性的比例分子，与 IsolationDivisor 配合。
        internal const int IsolationPositiveNumerator = 3;
        // 隔离比例分母；正值乘以分子再除以此值，非正值直接除以此值。
        internal const int IsolationDivisor = 4;
        // 学习加速节点使每次有效成长变为多少倍；不提高成长上限。
        internal const int LearningMultiplier = 2;
        // 每个渗透模组处理后污染物的剩余比例（0～1）；多个模组乘算叠加。
        internal const decimal OsmosisRemainingFraction = 0.75m;
        // 市场追踪的基础成功概率（%），实际概率再受机器性能修正。
        internal const float MarketTrackingBaseChancePercent = 25f;
        // 同类目标命中解码缓存时的实际作业速度倍率。
        internal const int DecodingCacheSpeedMultiplier = 2;
    }
}
