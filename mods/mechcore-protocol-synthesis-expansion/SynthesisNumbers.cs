
namespace MechcoreProtocol.SynthesisExpansion;

// 编译内置数值：修改本源码后，重新编译所属 Mod 才会生效。
// 本表不创建运行时配置；价格用信用点，尺寸用格数，Percent 用百分比，Bp 用基点（10000 = 100%）。
internal static class SynthesisNumbers
{
    /// <summary>合成扩展物品的占格和基础价值。</summary>
    /// <param name="Width">物品占用矩形的宽度（格）。</param>
    /// <param name="Height">物品占用矩形的高度（格）。</param>
    /// <param name="BaseValue">未叠加生产、品级等修正时的物品基础价值（信用点）。</param>
    /// <param name="ContrabandLevel">原生违禁等级：0 合法，1 低级，2 中级，3 高级，4 高危级。</param>
    internal readonly record struct ItemNumbers(int Width, int Height, int BaseValue, int ContrabandLevel = 0);
    /// <summary>机器物品占格、各仓尺寸和基础处理参数。</summary>
    /// <param name="QualityStep">质量效果的声明步长（质量点）；0 表示不使用按步长提升品级的模式。</param>
    /// <param name="Width">机器物品本身的宽度（格），不是输入／输出仓宽度。</param>
    /// <param name="Height">机器物品本身的高度（格），不是输入／输出仓高度。</param>
    /// <param name="InputWidth">机器输入仓宽度（格）。</param>
    /// <param name="InputHeight">机器输入仓高度（格）。</param>
    /// <param name="OutputWidth">机器输出仓宽度（格）。</param>
    /// <param name="OutputHeight">机器输出仓高度（格）。</param>
    /// <param name="ModuleWidth">机器模组舱宽度（格）。</param>
    /// <param name="ModuleHeight">机器模组舱高度（格）。</param>
    /// <param name="WaterSourceSlots">液体输入容器槽数量；0 表示没有液体输入槽。</param>
    /// <param name="BasePowerCost">每批基础电耗（电量单位）；实际耗电读取机器当前的原生状态。</param>
    internal readonly record struct MachineNumbers(int QualityStep, int Width, int Height, int InputWidth, int InputHeight, int OutputWidth, int OutputHeight, int ModuleWidth, int ModuleHeight, int WaterSourceSlots, int BasePowerCost);

    // 物品价格和占格；各字段单位见 ItemNumbers。
    internal static class Items
    {
        // R01 石英原料的基础价值和占格。
        internal static readonly ItemNumbers Quartz =
            new(Width: 3, Height: 2, BaseValue: 20);
        // R02 硅的基础价值和占格。
        internal static readonly ItemNumbers Silicon =
            new(Width: 2, Height: 2, BaseValue: 70);
        // R04 钛矿料的基础价值和占格。
        internal static readonly ItemNumbers TitaniumOre =
            new(Width: 3, Height: 2, BaseValue: 45);
        // R05 钛合金的基础价值和占格。
        internal static readonly ItemNumbers TitaniumAlloy =
            new(Width: 3, Height: 2, BaseValue: 153);
        // C01 精密零件的基础价值和占格。
        internal static readonly ItemNumbers PrecisionParts =
            new(Width: 2, Height: 2, BaseValue: 140);
        // C02 高级电路元件的基础价值和占格。
        internal static readonly ItemNumbers AdvancedCircuit =
            new(Width: 3, Height: 2, BaseValue: 185);
        // C03 控制芯片的基础价值和占格。
        internal static readonly ItemNumbers ControlChip =
            new(Width: 2, Height: 2, BaseValue: 427);
        // C06 电源调节组件的基础价值和占格。
        internal static readonly ItemNumbers PowerRegulator =
            new(Width: 3, Height: 2, BaseValue: 387);
        // C11 机械核心的基础价值和占格。
        internal static readonly ItemNumbers MechanicalCore =
            new(Width: 2, Height: 2, BaseValue: 1229);
        // Y01 AI 核心的基础价值和占格。
        internal static readonly ItemNumbers AiCore =
            new(Width: 2, Height: 2, BaseValue: 1939, ContrabandLevel: 4);
        // Y02 睡眠压缩模块的基础价值和占格。
        internal static readonly ItemNumbers SleepCompressionModule =
            new(Width: 2, Height: 1, BaseValue: 752);
        // Y03 动能回收的基础价值和占格。
        internal static readonly ItemNumbers KineticRecovery =
            new(Width: 2, Height: 3, BaseValue: 942);
        // Y04 钛合金外骨骼的基础价值和占格。
        internal static readonly ItemNumbers TitaniumExoskeleton =
            new(Width: 3, Height: 4, BaseValue: 673);
        // Y05 精细动作辅助器的基础价值和占格。
        internal static readonly ItemNumbers FineMotorAssistant =
            new(Width: 2, Height: 3, BaseValue: 1933);
        // Y06 高科技眼镜的基础价值和占格。
        internal static readonly ItemNumbers CyberEye =
            new(Width: 2, Height: 1, BaseValue: 1941);
        // Y07 机器猫；同类持有加成不叠加。
        internal static readonly ItemNumbers RobotCat =
            new(Width: 3, Height: 3, BaseValue: 2592);
        // S01 义体保管箱：外部占格；内部容量见 CyberneticStorage。
        internal static readonly ItemNumbers CyberneticStorageBox =
            new(Width: 3, Height: 3, BaseValue: 699);
        // A01 拾荒机器人：每台独立工作，内部容量继承合成用存储区。
        internal static readonly ItemNumbers ScavengingRobot =
            new(Width: 3, Height: 3, BaseValue: 2000);
        // T01 便携采矿钻：携带被动工具，不消耗电量或使用次数。
        internal static readonly ItemNumbers MiningDrill =
            new(Width: 2, Height: 2, BaseValue: 300);
        // K01 材料精炼机物品的基础价值和占格。
        internal static readonly ItemNumbers RefinerItem =
            new(Width: 3, Height: 3, BaseValue: 150);
        // K02 机电工作台物品的基础价值和占格。
        internal static readonly ItemNumbers SynthesizerItem =
            new(Width: 3, Height: 3, BaseValue: 200);
        // K03 精密装配机物品的基础价值和占格。
        internal static readonly ItemNumbers AssemblerItem =
            new(Width: 3, Height: 3, BaseValue: 300);
        // K05 全域制造终端物品的基础价值和占格。
        internal static readonly ItemNumbers UniversalManufacturerItem =
            new(Width: 3, Height: 3,
                BaseValue: 400);
        // G01 材料精炼手册的基础价值和占格。
        internal static readonly ItemNumbers RefiningGuideItem =
            new(Width: 2, Height: 3, BaseValue: 25);
        // G02 合成手册的基础价值和占格。
        internal static readonly ItemNumbers SynthesisGuideItem =
            new(Width: 2, Height: 3, BaseValue: 25);
        // G06 义体目录的基础价值和占格。
        internal static readonly ItemNumbers CyberneticCatalogItem =
            new(Width: 2, Height: 3, BaseValue: 25);

        // 按稳定物品代码读取对应数值表；未知代码直接拒绝。
        internal static ItemNumbers ForCode(string code) => code switch
        {
            "R01" => Quartz,
            "R02" => Silicon,
            "R04" => TitaniumOre,
            "R05" => TitaniumAlloy,
            "C01" => PrecisionParts,
            "C02" => AdvancedCircuit,
            "C03" => ControlChip,
            "C06" => PowerRegulator,
            "C11" => MechanicalCore,
            "Y01" => AiCore,
            "Y02" => SleepCompressionModule,
            "Y03" => KineticRecovery,
            "Y04" => TitaniumExoskeleton,
            "Y05" => FineMotorAssistant,
            "Y06" => CyberEye,
            "Y07" => RobotCat,
            "S01" => CyberneticStorageBox,
            "A01" => ScavengingRobot,
            "T01" => MiningDrill,
            "K01" => RefinerItem,
            "K02" => SynthesizerItem,
            "K03" => AssemblerItem,
            "K05" => UniversalManufacturerItem,
            "G01" => RefiningGuideItem,
            "G02" => SynthesisGuideItem,
            "G06" => CyberneticCatalogItem,
            _ => throw new ArgumentException("Unknown synthesis item code", nameof(code))
        };
    }

    internal static class CyberneticStorage
    {
        internal const int Width = 8;
        internal const int Height = 8;
    }

    internal static class MiningDrill
    {
        internal const int ExtraOreChancePercent = 10;
        internal const int UpgradedOreChancePercent = 20;
        internal const int UpgradeTitaniumAlloyCount = 1;
        internal const int MetalOreWeight = 50;
        internal const int TitaniumOreWeight = 25;
        internal const int QuartzWeight = 25;
    }

    internal static class ScavengingRobot
    {
        internal const int DefaultWidth = 8;
        internal const int DefaultHeight = 8;
        // 每台机器人每晚工作一次的固定电耗，与拾荒次数无关。
        internal const int NightlyPowerCost = 25;
        internal const int BreakdownChancePercent = 10;
        internal const int RepairIngotCount = 1;
        // 原版拾荒扫描仪初始 15%，每次升级增加 5 个百分点及 15 信用点基础价值。
        internal const int InitialScannerChance = 15;
        internal const int ScannerUpgradeStep = 5;
        internal const int UpgradeElectronicCount = 1;
        internal const int UpgradeValueBonus = 15;
    }

    // 六台机器的独立数值；输入／输出仓、模组舱尺寸均使用格数。
    internal static class Machines
    {
        // 性能每满此点数可额外加工一批；全域制造终端不使用额外批次。
        internal const int PerformancePerExtraBatch = 100;

        // 机器定义未明确提供液体槽数量时的默认值。
        internal const int DefaultWaterSourceSlots = 0;
        // 机器定义未明确提供基础电耗时的默认电量单位数。
        internal const int DefaultPowerCost = 8;
        // K01 材料精炼机的基础参数和仓库尺寸。
        internal static readonly MachineNumbers K01 =
            new(
                QualityStep: 100,
                Width: Items.RefinerItem.Width,
                Height: Items.RefinerItem.Height,
                InputWidth: 9,
                InputHeight: 6,
                OutputWidth: 9,
                OutputHeight: 6,
                ModuleWidth: 7,
                ModuleHeight: 5,
                WaterSourceSlots: 0,
                BasePowerCost: 10);
        // K02 机电工作台的基础参数和仓库尺寸。
        internal static readonly MachineNumbers K02 =
            new(
                QualityStep: 100,
                Width: Items.SynthesizerItem.Width,
                Height: Items.SynthesizerItem.Height,
                InputWidth: 9,
                InputHeight: 6,
                OutputWidth: 9,
                OutputHeight: 6,
                ModuleWidth: 7,
                ModuleHeight: 5,
                WaterSourceSlots: 0,
                BasePowerCost: 15);
        // K03 精密装配机的基础参数和仓库尺寸。
        internal static readonly MachineNumbers K03 =
            new(
                QualityStep: 100,
                Width: Items.AssemblerItem.Width,
                Height: Items.AssemblerItem.Height,
                InputWidth: 9,
                InputHeight: 6,
                OutputWidth: 9,
                OutputHeight: 6,
                ModuleWidth: 7,
                ModuleHeight: 5,
                WaterSourceSlots: 0,
                BasePowerCost: 20);
        // K05 全域制造终端的基础参数和仓库尺寸。
        internal static readonly MachineNumbers K05 =
            new(
                QualityStep: 0,
                Width: Items.UniversalManufacturerItem.Width,
                Height: Items.UniversalManufacturerItem.Height,
                InputWidth: 9,
                InputHeight: 6,
                OutputWidth: 9,
                OutputHeight: 6,
                ModuleWidth: 7,
                ModuleHeight: 5,
                WaterSourceSlots: 0,
                BasePowerCost: 25);
    }

    internal static class Distribution
    {
        // 普通白天供货的 Mod 总权重由 Forge 分配，以下权重用于拾荒及矿工。
        // 七种已加工材料／组件从原生第 22 天起参加供货。
        internal const int RefinedMaterialMinimumStockDay = 22;
        // 材料加入原生材料表时的相对权重；不是固定掉落概率。
        internal const float MaterialLootWeight = 0.125f;
        // 原版材料表八种材料各为 125；两种原矿以相同权重参加拾荒与 NPC 材料表抽取。
        internal const float OreLootWeight = 125f;
        // 矿工整批选矿使用独立权重，保留原版 70%、石英和钛矿料各 15%。
        internal const float OreMinerWeight = 0.5f;
        // 机器加入原生工具表时的相对权重。
        internal const float MachineLootWeight = 0.05f;
        // 夜间商店每次生成库存时，各台机器独立判定的上架概率（百分比）。
        internal const int MachineNightShopChancePercent = 40;
        // 日用品成品及包装饮品也可流通；最终义体和手册排除随机掉落。
        internal const float HouseholdLootWeight = 0.05f;
        internal const float FoodLootWeight = 0.1f;
    }

    // 各配方每批的材料件数、产量和最低品级；既有完整配方 JSON 可按原规则覆盖。
    internal static class Recipes
    {
        // P01：硅配方，以下数量均按单批计算。
        internal static class P01
            {
                // 每批消耗的石英数量（件）。
                internal const int QuartzCount = 2;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // P02：钛合金配方，以下数量均按单批计算。
        internal static class P02
            {
                // 每批消耗的钛矿料数量（件）。
                internal const int TitaniumOreCount = 1;
                // 每批消耗的金属锭数量（件）。
                internal const int MetalIngotCount = 1;
                // 材料要求使用的最低金属纯度等级（High 枚举）；具体作用材料由配方声明决定。
                internal const int MinimumMetalPurity = (int)Definitions.IngotPurityGrade.High;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // P03：精密零件配方，以下数量均按单批计算。
        internal static class P03
            {
                // 每批消耗的金属锭数量（件）。
                internal const int MetalIngotCount = 2;
                // 材料要求使用的最低金属纯度等级（High 枚举）；具体作用材料由配方声明决定。
                internal const int MinimumMetalPurity = (int)Definitions.IngotPurityGrade.High;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // P04：高级电路元件配方，以下数量均按单批计算。
        internal static class P04
            {
                // 每批消耗的硅数量（件）。
                internal const int SiliconCount = 1;
                // 每批消耗的普通电子元件数量（件）。
                internal const int CommonElectronicCount = 2;
                // 材料要求使用的最低金属纯度等级（High 枚举）；具体作用材料由配方声明决定。
                internal const int MinimumMetalPurity = (int)Definitions.IngotPurityGrade.High;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // P15：普通电子元件配方，以下数量均按单批计算。
        internal static class P15
        {
            internal const int WireCount = 1;
            internal const int PrinterPlasticCount = 1;
            internal const int OutputCount = 1;
        }

        // P16：空捕鼠夹配方，以下数量均按单批计算。
        internal static class P16
        {
            internal const int MetalIngotCount = 1;
            internal const int WireCount = 1;
            internal const int OutputCount = 1;
        }

        // P06：控制芯片配方，以下数量均按单批计算。
        internal static class P06
            {
                // 每批消耗的高级电路元件数量（件）。
                internal const int AdvancedCircuitCount = 1;
                // 每批消耗的精密零件数量（件）。
                internal const int PrecisionPartsCount = 1;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // P09：电源调节组件配方，以下数量均按单批计算。
        internal static class P09
            {
                // 每批消耗的精密零件数量（件）。
                internal const int PrecisionPartsCount = 1;
                // 每批消耗的钛合金数量（件）。
                internal const int TitaniumAlloyCount = 1;
                // 每批消耗的线缆数量（件）。
                internal const int WireCount = 1;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // P14：机械核心配方，以下数量均按单批计算。
        internal static class P14
            {
                // 每批消耗的控制芯片数量（件）。
                internal const int ControlChipCount = 1;
                // 每批消耗的电源调节组件数量（件）。
                internal const int PowerRegulatorCount = 1;
                // 每批消耗的钛合金数量（件）。
                internal const int TitaniumAlloyCount = 1;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }


        internal static class M07
        {
            internal const int AiCoreCount = 1;
            internal const int PrecisionPartsCount = 1;
            internal const int TitaniumAlloyCount = 1;
            internal const int OutputCount = 1;
        }
        internal static class M08
        {
            internal const int TitaniumAlloyCount = 1;
            internal const int PrecisionPartsCount = 1;
            internal const int MetalIngotCount = 1;
            internal const int OutputCount = 1;
        }
        internal static class M09
        {
            internal const int MechanicalCoreCount = 1;
            internal const int ScannerCount = 1;
            internal const int StorageAreaCount = 1;
            internal const int OutputCount = 1;
        }
        internal static class M10
        {
            internal const int MetalIngotCount = 1;
            internal const int MinimumMetalPurity = 8;
            internal const int PrecisionPartsCount = 1;
            internal const int CommonElectronicCount = 1;
            internal const int OutputCount = 1;
        }

        // M01：高科技眼镜配方，以下数量均按单批计算。
        internal static class M01
            {
                // 每批消耗的机械核心数量（件）。
                internal const int MechanicalCoreCount = 1;
                // 每批消耗的电源调节组件数量（件）。
                internal const int PowerRegulatorCount = 1;
                // 每批消耗的放大镜数量（件）。
                internal const int MagnifierCount = 1;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // M02：AI 核心配方，以下数量均按单批计算。
        internal static class M02
            {
                // 每批消耗的机械核心数量（件）。
                internal const int MechanicalCoreCount = 1;
                // 每批消耗的高级电路元件数量（件）。
                internal const int AdvancedCircuitCount = 1;
                // 每批消耗的system_capped_neural_core 原版神经核心数量（件）。
                internal const int SystemCappedNeuralCoreCount = 1;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // M03：睡眠压缩模块配方，以下数量均按单批计算。
        internal static class M03
            {
                // 每批消耗的控制芯片数量（件）。
                internal const int ControlChipCount = 1;
                // 每批消耗的高级电路元件数量（件）。
                internal const int AdvancedCircuitCount = 1;
                // 每批消耗的咖啡因药片数量（件）。
                internal const int CaffeinePillCount = 2;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // M04：动能回收配方，以下数量均按单批计算。
        internal static class M04
            {
                // 每批消耗的电源调节组件数量（件）。
                internal const int PowerRegulatorCount = 1;
                // 每批消耗的energy_credit_ext 原版材料数量（件）。
                internal const int EnergyCreditExtCount = 1;
                // 每批消耗的连续节能模组数量（件）。
                internal const int ContinuousEfficiencyModuleCount = 1;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // M05：钛合金外骨骼配方，以下数量均按单批计算。
        internal static class M05
            {
                // 每批消耗的钛合金数量（件）。
                internal const int TitaniumAlloyCount = 1;
                // 每批消耗的精密零件数量（件）。
                internal const int PrecisionPartsCount = 2;
                // 每批消耗的连续加速模组数量（件）。
                internal const int ContinuousAccelerationModuleCount = 1;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }

        // M06：精细动作辅助器配方，以下数量均按单批计算。
        internal static class M06
            {
                // 每批消耗的机械核心数量（件）。
                internal const int MechanicalCoreCount = 1;
                // 每批消耗的精密零件数量（件）。
                internal const int PrecisionPartsCount = 2;
                // 每批消耗的连续校准模组数量（件）。
                internal const int ContinuousCalibrationModuleCount = 1;
                // 每批固定产物数量（件），不随质量属性增加。
                internal const int OutputCount = 1;
            }
    }

    // 材料精炼机的玻璃回收配方；输入数量按件，输出均为石英。
    internal static class Glass
    {
        // 啤酒瓶回收配方每批消耗的啤酒瓶数量（件）。
        internal const int BeerBottleInputCount = 1;
        // 啤酒瓶回收配方每批产出的石英数量（件）。
        internal const int BeerBottleOutputCount = 1;
        // 玻璃刀回收配方每批消耗的玻璃刀数量（件）。
        internal const int GlassShivInputCount = 2;
        // 玻璃刀回收配方每批产出的石英数量（件）。
        internal const int GlassShivOutputCount = 1;
        // 玻璃碎片回收配方每批消耗的玻璃碎片数量（件）。
        internal const int GlassShardInputCount = 2;
        // 玻璃碎片回收配方每批产出的石英数量（件）。
        internal const int GlassShardOutputCount = 1;
        // 葡萄酒瓶回收配方每批消耗的葡萄酒瓶数量（件）。
        internal const int WineBottleInputCount = 1;
        // 葡萄酒瓶回收配方每批产出的石英数量（件）。
        internal const int WineBottleOutputCount = 1;
    }

    // 各机器在材料实际内在价值之上增加的生产价值百分比；100 表示加价 100%。
    internal static class ProductionMarkups
    {
        // 材料精炼机的默认生产价值加成（%）。
        internal const int K01 = 100;
        // 机电工作台的默认生产价值加成（%）。
        internal const int K02 = 40;
        // 精密装配机的默认生产价值加成（%）。
        internal const int K03 = 30;
        // 全域制造终端的默认生产价值加成（%），也用于追加配方。
        internal const int K05 = 25;
    }

    internal static class Quality
    {
        // 固体组件每提升一个品级所需的机器质量点数。
        internal const int PointsPerComponentGrade = 100;
        // 精炼产物每提升一级纯度所需的机器质量点数。
        internal const int PointsPerPurityGrade = 100;
        // 材料精炼机炼锭在原版矿石／废金属基础纯度之上的默认提升等级。
        internal const int MetalSmeltingBasePurityLevels = 1;
        // 普通组件在保存的生产基础价值上增加的价值百分比。
        internal const int OrdinaryValueBonusPercent = 0;
        // 精良组件在保存的生产基础价值上增加的价值百分比。
        internal const int FineValueBonusPercent = 15;
        // 卓越组件在保存的生产基础价值上增加的价值百分比。
        internal const int ExcellentValueBonusPercent = 30;
        // 完美组件在保存的生产基础价值上增加的价值百分比。
        internal const int PerfectValueBonusPercent = 50;
    }

    internal static class Effects
    {
        internal const int RobotCatAttractivenessBonus = 500;
        // 高科技眼镜扫描可出鉴定书物品时消耗的电量单位。
        internal const int EyeScanBatteryCost = 15;
        // 不出鉴定书的放大镜检查，包括真伪与果实状态。
        internal const int EyeInspectionBatteryCost = 5;
        // 带义体标签的物品卖给伪人顾客时，原生市场阶段售价修正增加量（百分点）。
        internal const int CyberneticAugSaleBonusPercent = 25;
        // 伪人顾客的原生购买预算倍率，在预算初始化或首次转为伪人时应用。
        internal const int AugCustomerBudgetMultiplier = 2;
        // AI 核心对非伪人顾客的售价修正增加量（百分点）。
        internal const int AiCoreSaleBonusPercent = 10;
        // 持有睡眠压缩模块时额外提供的夜间行动次数。
        internal const int SleepExtraNightActions = 1;
        // 动能回收提供的机器效率加成（百分点）。
        internal const int KineticEfficiencyBonusPercent = 50;
        // 钛合金外骨骼提供的机器性能加成（百分点）。
        internal const int ExoskeletonPerformanceBonusPercent = 50;
        // 精细动作辅助器提供的机器质量加成（百分点）。
        internal const int FineMotorQualityBonusPercent = 50;
    }

    internal static class NativeInspectionValues
    {
        // Formal 0.46D Build 26274380 ItemConditionActionList values.
        // Inspection reads these values without invoking condition actions.
        internal const int GenuineImmunivax = 160;
        internal const int ExpiredImmunivax = 80;
        internal const int CounterfeitImmunivax = 40;
        internal const int UnusableImmunivax = 8;
        internal const int GenuineCigarette = 100;
        internal const int CounterfeitCigarette = 50;
        internal const int CounterfeitStamp = 8;
        // InsStampHelper.SetGenuine 按票面额度乘 0.75 后向零截断。
        internal const double GenuineStampValueFactor = 0.75;
    }

    internal static class RecipeLimits
    {
        // 配方配置允许的机器生产价值加成百分比上限。
        internal const int MaxProductionMarkupPercent = 1000;
    }

    internal static class Validation
    {
        // 合成扩展物品定义允许的最大占格宽度（格）。
        internal const int MaxItemWidth = 6;
        // 合成扩展物品定义允许的最大占格高度（格）。
        internal const int MaxItemHeight = 4;
        // 新机器单批产物上限；加载配置时与实际可处理范围一起校验。
        internal const int MaxMachineOutputCount = 24;
        // 玻璃回收配方允许的最大基础产物件数。
        internal const int MaxRecipeOutputCount = 256;
    }
}
