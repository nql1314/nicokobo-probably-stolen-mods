namespace AugMechanicalAscension;

// 编译内置数值：修改本源码后，重新编译所属 Mod 才会生效。
// 本表不创建运行时配置；价格用信用点，尺寸用格数，Percent 用百分比，Bp 用基点（10000 = 100%）。
internal static class AugNumbers
{
    internal static class Distribution
    {
        // 修补胶进入原生医疗掉落表；白天供货共享 Forge 的 Mod 总权重。
        internal const float RepairGelLootWeight = 0.125f;
    }

    /// <summary>义体成品的占格、基础价值和制造液体需求。</summary>
    /// <param name="Width">物品外接矩形的宽度（格）。</param>
    /// <param name="Height">物品外接矩形的高度（格）。</param>
    /// <param name="BaseValue">成品未叠加生产价值修正时的基础价值（信用点）。</param>
    /// <param name="ContrabandLevel">原生违禁等级：0 合法，1 低级，2 中级，3 高级，4 高危级。</param>
    internal readonly record struct ProductNumbers(int Width, int Height, int BaseValue, int ContrabandLevel = 0);

    // 新周目的内置默认值；已有运行时 JSON 和存档快照仍按原规则覆盖。
    internal static class OpeningAndSurvival
    {
        // 开局信用点余额。
        internal const int InitialCredits = 500;
        internal const int NativeStartingCredits = 600;
        // 开局租金（信用点）。
        internal const int InitialRent = 250;
        // 开局治安部声望增减量，叠加到原有声望；负数表示扣减。
        internal const int InitialSecurityReputation = -15;
        // 默认开局电池数量（件）。
        internal const int InitialBatteryCount = 1;
        // 默认开局医疗用品数量（件）。
        internal const int InitialMedicalCount = 1;
        // 默认开局电子元件数量（件）。
        internal const int InitialElectronicCount = 1;
        // 开局健康值（健康点）。
        internal const int InitialHealth = 100;
        // 正常玩法中的健康上限（健康点）。
        internal const int MaxHealth = 100;
        // 未应用义体修正时，每日扣除的健康点数。
        internal const int DailyHealthLoss = 10;
        // 开局暴露值（暴露点）。
        internal const int InitialExposure = 20;
        // 玩法中的暴露上限（暴露点）。
        internal const int MaxExposure = 100;
        // 未应用义体修正时，每日增加的暴露点数。
        internal const int DailyExposureGain = 5;
        // 开局免除每日暴露增长的天数；0 表示无宽限期。
        internal const int ExposureGraceDays = 0;
        // 低暴露档对非同类顾客的基础售价扣减（基点，10000 = 100%）；售价档位对已有周目同样生效。
        internal const int BargainBasePenaltyBp = 1_000;
        // 第一档售价惩罚开始生效的编译暴露阈值（暴露点）。
        internal const int BargainThreshold1 = 35;
        // 第二档售价惩罚开始生效的编译暴露阈值（暴露点）。
        internal const int BargainThreshold2 = 75;
        // 配置快照保留的第三档暴露阈值；当前议价计算使用前两档阈值。
        internal const int BargainThreshold3 = 100;
        // 达到第一档暴露阈值时，对非同类顾客的售价扣减（基点）。
        internal const int BargainPenalty1Bp = 2_500;
        // 达到第二档暴露阈值时，对非同类顾客的售价扣减（基点）。
        internal const int BargainPenalty2Bp = 5_000;
        // 配置快照保留的第三档售价扣减（基点）；当前议价计算使用前两档惩罚。
        internal const int BargainPenalty3Bp = 5_000;
        // 与同类顾客交易时的售价加成（基点）。
        internal const int SameKindBonusBp = 2_500;
        // AI 助手对非同类顾客的售价补偿（基点）；字段沿用 Voice 命名。
        internal const int VoiceBonusBp = 1_000;
        // 医疗补给目标库存应覆盖的总恢复量（健康点）；按单件恢复量向上取整为件数。
        internal const int MedicalRecoveryBudget = 20;
        // 电子元件每轮补货后的目标库存（件）。
        internal const int ElectronicSupplyTargetStock = 7;
        // 电子元件补货周期（游戏天数）。
        internal const int ElectronicSupplyCycleDays = 7;
        // 补给售价相对标准单价的倍率（基点）；10000 表示原价。
        internal const int SupplyPriceMultiplierBp = 12_000;
        // 使用一件电子元件修复时降低的暴露点数。
        internal const int ExposureRepairPerElectronic = 25;
        // 脑机接口测试价格（信用点）；快照中的值偏离此默认值时可覆盖正常翻倍价格。
        internal const long NeuralModuleTestPrice = 100;
        // 鉴定规则要求累计完成的完美鉴定次数。
        internal const int AppraisalPerfectRequired = 3;
        // 一次高科技眼镜扫描消耗的电量单位。
        internal const int EyeScanBatteryCost = 15;
    }

    // 义体配置的解锁费用与效果默认值；费用不是制造成品的基础售价。
    internal static class Cybernetics
    {
        // 睡眠控制义体的配置解锁费用（信用点）。
        internal const long SleepControl = 250;
        // 人工脚踝的配置解锁费用（信用点）。
        internal const long ArtificialAnkle = 300;
        // 红外视觉的配置解锁费用（信用点）。
        internal const long InfraredVision = 350;
        // 高科技眼镜的配置解锁费用（信用点）。
        internal const long CyberEye = 450;
        // 全息通讯器的配置解锁费用（信用点）。
        internal const long CommunicationReceiver = 300;
        // 动能回收的配置解锁费用（信用点）。
        internal const long KineticRecovery = 550;
        // 钛骨骼的配置解锁费用（信用点）。
        internal const long TitaniumSkeleton = 550;
        // 精细动作辅助器的配置解锁费用（信用点）。
        internal const long FineMotorAssistant = 550;
        // 合成声带的配置解锁费用（信用点）。
        internal const long SyntheticVocalCords = 350;
        // 人工心脏的配置解锁费用（信用点）。
        internal const long ArtificialHeart = 600;
        // 拟态皮肤的配置解锁费用（信用点）。
        internal const long MimeticSkin = 500;
        // AI 助手核心的配置解锁费用（信用点）。
        internal const long AiAssistantCore = 700;
        // 未声明独立材料配方时，解锁默认消耗的材料件数。
        internal const int UnlockMaterialCount = 1;
        // 睡眠控制额外提供的夜间行动次数。
        internal const int SleepExtraNightActions = 1;
        // 人工脚踝额外提供的拾荒尝试次数。
        internal const int AnkleExtraScavAttempts = 3;
        // 红外视觉启用后的受伤概率倍率（基点）；7500 表示原概率的 75%。
        internal const int InfraredWoundChanceMultiplierBp = 7_500;
        // 义体提供的机器效率加成（基点）。
        internal const int MachineEfficiencyBonusBp = 5_000;
        // 义体提供的机器性能加成（基点）。
        internal const int MachinePerformanceBonusBp = 5_000;
        // 义体提供的机器质量加成（基点）。
        internal const int MachineQualityBonusBp = 5_000;
        // 合成声带使售价惩罚暴露阈值上移的点数。
        internal const int ExposureThresholdShift = 20;
        // 人工心脏启用后的每日健康损失倍率（基点）；5000 表示损失减半。
        internal const int ArtificialHeartHealthLossMultiplierBp = 5_000;
        // 拟态皮肤每天抵消的暴露增长点数。
        internal const int SkinDailyExposureReduction = 3;
    }

    // 人工心脏：每批制造的材料数量。
    internal static class RecipeArtificialHeart
    {
        // 每批消耗的精密零件数量（件）。
        internal const int PrecisionPartsCount = 1;
        // 每批消耗的电源调节组件数量（件）。
        internal const int PowerRegulatorCount = 1;
        // 每批消耗的机械核心数量（件）。
        internal const int MechanicalCoreCount = 1;
        // 每批消耗的一袋蓝血数量（件）。
        internal const int BlueBloodBagCount = 1;
    }

    // 拟态皮肤：每批制造的材料数量。
    internal static class RecipeMimeticSkin
    {
        // 每批消耗的罗非鱼植皮片数量（件）。
        internal const int SkinGraftCount = 2;
        // 每批消耗的韧棉织物数量（件）。
        internal const int KottonFabricCount = 1;
        // 每批消耗满使用次数的 neuroactive_perfume 原版材料数量（瓶）。
        internal const int NeuroactivePerfumeCount = 1;
    }

    // 人工脚踝：每批制造的材料数量。
    internal static class RecipeArtificialAnkle
    {
        // 每批消耗的精密零件数量（件）。
        internal const int PrecisionPartsCount = 1;
        // 每批消耗的钛合金数量（件）。
        internal const int TitaniumAlloyCount = 1;
        // 每批消耗的 nuts_metal 原版材料数量（件）。
        internal const int NutsMetalCount = 2;
    }

    // 红外视觉：每批制造的材料数量。
    internal static class RecipeInfraredVision
    {
        // 每批消耗的高级电路元件数量（件）。
        internal const int AdvancedCircuitCount = 1;
        // 每批消耗的硅数量（件）。
        internal const int SiliconCount = 1;
        // 每批消耗的手电筒数量（件）。
        internal const int FlashlightCount = 1;
    }

    // 全息通讯器：每批制造的材料数量。
    internal static class RecipeCommunicationReceiver
    {
        // 每批消耗的控制芯片数量（件）。
        internal const int ControlChipCount = 1;
        // 每批消耗的高级电路元件数量（件）。
        internal const int AdvancedCircuitCount = 1;
        // 每批消耗的 alarm_module_transmitter 原版警报模组数量（件）。
        internal const int AlarmModuleTransmitterCount = 1;
    }

    // 合成声带：每批制造的材料数量。
    internal static class RecipeSyntheticVocalCords
    {
        // 每批消耗的精密零件数量（件）。
        internal const int PrecisionPartsCount = 1;
        // 每批消耗的控制芯片数量（件）。
        internal const int ControlChipCount = 1;
        // 每批消耗的音乐磁带数量（盘）；任一有效音乐内容均可。
        internal const int MusicCassetteCount = 1;
    }

    internal static class RecipeCognitiveTurbo
    {
        internal const int AiCoreCount = 1;
        internal const int PowerRegulatorCount = 1;
        internal const int AdvancedTurboBoosterCount = 1;
    }

    // 义体成品的数值；各字段的单位见 ProductNumbers。
    internal static class Products
    {
        // 人工心脏的占格与基础价值。
        // ceil((精密零件 140 + 电源调节组件 387 + 机械核心 1229 + 原版蓝血袋 250) * 1.15 + 25)。
        internal static readonly ProductNumbers ArtificialHeart =
            new(Width: 2, Height: 3, BaseValue: 2332);
        // 拟态皮肤的占格与基础价值。
        // ceil((植皮片 24×2 + 韧棉织物 65 + 同步香水 150) * 1.25 + 25)。
        internal static readonly ProductNumbers MimeticSkin =
            new(Width: 2, Height: 3, BaseValue: 354, ContrabandLevel: 1);
        // 人工脚踝的占格与基础价值。
        internal static readonly ProductNumbers ArtificialAnkle =
            new(Width: 2, Height: 3, BaseValue: 366);
        // 红外视觉的占格与基础价值。
        internal static readonly ProductNumbers InfraredVision =
            new(Width: 1, Height: 1, BaseValue: 322);
        // 全息通讯器的占格与基础价值。
        internal static readonly ProductNumbers CommunicationReceiver =
            new(Width: 2, Height: 1, BaseValue: 729, ContrabandLevel: 1);
        // 合成声带的占格与基础价值。
        internal static readonly ProductNumbers SyntheticVocalCords =
            new(Width: 2, Height: 2, BaseValue: 1683, ContrabandLevel: 1);
        // ceil((AI core 1939 + power regulator 387 + advanced turbo 300) * 1.15 + 25).
        internal static readonly ProductNumbers CognitiveTurbo =
            new(Width: 2, Height: 2, BaseValue: 3045);
    }

    internal static class BionicRepairGel
    {
        internal const int Width = 1;
        internal const int Height = 2;
        // ceil((blood bag 33 + bandage 8) * 1.25 + K04 base power 10).
        internal const int BaseValue = 62;
        internal const int BloodBagCount = 1;
        internal const int BandageCount = 1;
        internal const int OutputCount = 1;
    }

    // 脑机接口的持有上限、翻倍价格和随持有数量变化的基础属性。
    internal static class NeuralInterface
    {
        // 脑机接口使用原生高级违禁品规则。
        internal const int ContrabandLevel = 3;
        // 同一周目允许持有的脑机接口总数上限，包含开局赠送的一件。
        internal const int MaximumOwned = 25;
        // 价格翻倍计算允许的最大左移位数，用于溢出保护。
        internal const int MaxPriceShift = 30;
        // 脑机接口基础价值（信用点）；购买价按此值乘以 2 的已持有数量次方，扣除成就奖励数量。
        internal const long BasePrice = 100;
        // 每多持有一件接口，每件接口的基础性能增加的百分点。
        internal const int PerformancePerOwned = 8;
        // 每多持有一件接口，每件接口的基础效率增加的百分点。
        internal const int EfficiencyPerOwned = 8;
        // 每多持有一件接口，每件接口的基础质量增加的百分点。
        internal const int QualityPerOwned = 4;
        // 脑机接口超频效果使用的属性加成百分比。
        internal const int OverclockPercent = 25;
    }

    // 开局实际发放的物品数量；三个补给数量引用同一默认值。
    internal static class OpeningItems
    {
        // 开局赠送的脑机接口数量（件）。
        internal const int NeuralInterfaceCount = 1;
        // 开局赠送的材料精炼机数量（件）。
        internal const int MaterialRefinerCount = 1;
        // 开局电池数量（件），与生存配置的默认数量保持一致。
        internal const int BatteryCount = OpeningAndSurvival.InitialBatteryCount;
        // 开局医疗用品数量（件），与生存配置的默认数量保持一致。
        internal const int MedicalCount = OpeningAndSurvival.InitialMedicalCount;
        // 开局电子元件数量（件），与生存配置的默认数量保持一致。
        internal const int ElectronicCount = OpeningAndSurvival.InitialElectronicCount;
        // 开局赠送的废金属数量（件）。
        internal const int ScrapMetalCount = 10;
        // 其中预先放入开局材料精炼机输入仓的数量（件）。
        internal const int RefinerScrapMetalCount = 9;
        // 开局赠送的材料霓虹灯牌数量（件）。
        internal const int MaterialSignCount = 1;
    }

    internal static class SaveFileFingerprintLimits
    {
        // 计算存档文件指纹时允许读取的最大文件大小（字节）。
        internal const long MaxProbeBytes = 16 * 1024 * 1024;
    }

    internal static class SupplyPurchaseRulesLimits
    {
        // 同一周目保留的补给购买回执数量上限。
        internal const int MaxReceipts = 512;
    }

    internal static class BargainRuntimeFlowLimits
    {
        // 议价运行诊断最多输出的日志行数。
        internal const int MaxDiagnosticLines = 60;
    }

    internal static class ConfigSnapshotLoaderLimits
    {
        // 读取既有外部配置 JSON 时允许的最大文件大小（字节）。
        internal const int MaxBytes = 65_536;
        // 外部配置 JSON 中允许解析的设置项总数上限。
        internal const int MaxSettings = 512;
    }

    internal static class Es3SlotReadbackLimits
    {
        // ES3 存档槽位文件读回时允许的最大文件大小（字节）。
        internal const int MaxBytes = 16 * 1024 * 1024;
    }

    internal static class RunStateCodecLimits
    {
        // 机械飞升周目 JSON 允许的最大 UTF-8 大小（字节）。
        internal const int MaxJsonBytes = 262_144;
        // 预留给待处理扫描状态的序列化空间（字节）。
        internal const int PendingScanReserveBytes = 4_096;
    }

    // 探针、界面、物品交付和错误日志的刷新／重试间隔；按字段后缀使用秒或毫秒。
    internal static class Timing
    {
        // 菜单状态探针的检查间隔（秒）。
        internal const int MenuProbeIntervalSeconds = 2;
        // 开局流程探针的检查间隔（毫秒）。
        internal const int StartFlowProbeIntervalMilliseconds = 500;
        // 开局流程探针失败后的重试间隔（秒）。
        internal const int StartFlowProbeFailureRetrySeconds = 15;
        // 每日状态对账错误日志的最小输出间隔（秒）。
        internal const int DayReconciliationErrorLogIntervalSeconds = 30;
        // 访客暴露处理错误日志的最小输出间隔（秒）。
        internal const int ExposureVisitErrorLogIntervalSeconds = 30;
        // 脑机接口基础属性变化的检查间隔（秒）。
        internal const int NeuralBaseRefreshIntervalSeconds = 1;
        // 脑机接口基础属性刷新失败后的重试间隔（秒）。
        internal const int NeuralBaseRefreshFailureRetrySeconds = 10;
        // Nico 名片交付的检查间隔（毫秒）。
        internal const int BusinessCardDeliveryIntervalMilliseconds = 100;
        // 库存无空间时再次尝试交付名片的间隔（秒）。
        internal const int BusinessCardNoSpaceRetrySeconds = 5;
        // 名片交付发生异常后的重试间隔（秒）。
        internal const int BusinessCardFailureRetrySeconds = 1;
        // 议价本地化文本的刷新间隔（秒）。
        internal const int BargainLocalizationRefreshSeconds = 1;
        // 状态浮层错误日志的最小输出间隔（秒）。
        internal const int StatusOverlayErrorLogIntervalSeconds = 30;
        // 未找到原版状态页时再次查找的间隔（秒）。
        internal const int StatusPageFindRetrySeconds = 2;
        // 状态页接入失败后的重试间隔（秒）。
        internal const int StatusPageFailureRetrySeconds = 5;
        // 状态页错误日志的最小输出间隔（秒）。
        internal const int StatusPageErrorLogIntervalSeconds = 30;
        // 状态页可见时，刷新义体物品所有权的间隔（秒）。
        internal const int StatusPageEffectRefreshIntervalSeconds = 1;
    }

    // 访客实际到店时的暴露变化，以及保安检查概率。
    internal static class ExposureVisits
    {
        // 保安阵营访客每次实际到店增加的暴露点数。
        internal const int SecurityExposureGain = 5;
        // 允许触发保安检查的最低暴露值（包含此值）。
        internal const int InspectionMinimumExposure = 75;
        // 允许触发保安检查的暴露上界（不包含此值）。
        internal const int InspectionMaximumExposureExclusive = 100;
        // 保安检查概率的减项；符合暴露区间时，概率百分比 = 暴露值 - 此值。
        internal const int InspectionChanceOffset = 70;
    }

    internal static class AugInteractions
    {
        // 成功威胁一位实际伪人顾客增加的暴露点数。
        internal const int ThreatExposureGain = 5;
        // 实际击杀一位伪人顾客增加的暴露点数。
        internal const int KillExposureGain = 10;
        // 原生伪人指数保持大于零，避免停止生成伪人。
        internal const int MinimumPresence = 1;
        // 原生伪人指数不能达到 100 的失败阈值。
        internal const int MaximumPresence = 99;
    }

    internal static class Workshop
    {
        // 工坊最终机械飞升节点消耗的信用点数量。
        internal const int MechanicalAscensionCreditCost = 100_000;
    }

    // 输入与存档的校验保护上限；这些上限不代表正常玩法中的最大值。
    internal static class Validation
    {
        // 物品、操作和记录标识符允许的最大字符数。
        internal const int MaxIdentifierChars = 128;
        // 单次鉴定结果允许记录的事实条目数上限。
        internal const int MaxFindings = 32;
        // 同一周目允许保存的鉴定提交记录数上限。
        internal const int MaxAppraisalSubmissions = 1_024;
        // 同一周目允许保存的扫描回执数上限。
        internal const int MaxScanReceipts = 512;
        // 同一周目允许保存的已扫描物品记录数上限。
        internal const int MaxScannedItems = 512;
        // 可保留的鉴定扫描缓存条目数上限。
        internal const int MaxScanCacheEntries = 128;
        // 电池电量观测值的校验上限（电量单位）。
        internal const int MaxBatteryCharge = 1_000_000;
        // 单次电池扣电请求的校验上限（电量单位）。
        internal const int MaxBatteryDebit = 1_000;
        // 鉴定得到的物品基础价值校验上限（信用点）。
        internal const long MaxAppraisedBaseValue = 1_000_000_000_000;
        // 单次物品发放请求的数量校验上限（件）。
        internal const int MaxGrantUnits = 1_000;
        // 已有补给物品总数量的校验上限（件）。
        internal const int MaxOwnedSupplyUnits = 1_000_000;
        // 开局或原版租金观测值的校验上限（信用点）。
        internal const int MaxRent = 1_000_000;
        // 保安阵营声望绝对值的校验上限。
        internal const int MaxSecurityReputationMagnitude = 1_000;
        // 记录中的游戏天数校验上限。
        internal const int MaxGameDay = 1_000_000;
        // 单种补给库存数量的校验上限（件）。
        internal const int MaxSupplyStock = 10_000;
        // 健康配置和记录的校验上限（健康点），不是玩法健康上限。
        internal const int MaxHealth = 10_000;
        // 暴露配置和记录的校验上限（暴露点），不是玩法暴露上限。
        internal const int MaxExposure = 10_000;
        // 暴露宽限期配置允许的最大天数。
        internal const int MaxExposureGraceDays = 100;
        // 医疗补给恢复量预算允许的最大健康点数。
        internal const int MaxRecoveryBudget = 10_000;
        // 补给周期配置允许的最大游戏天数。
        internal const int MaxSupplyCycleDays = 365;
        // 补给价格倍率允许的最大基点数。
        internal const int MaxSupplyPriceMultiplierBp = 100_000;
        // 脑机接口测试价格配置允许的最大信用点数。
        internal const long MaxNeuralTestPrice = 1_000_000_000;
        // 完美鉴定次数要求允许的最大值。
        internal const int MaxPerfectAppraisalsRequired = 100;
        // 高科技眼镜扫描耗电配置允许的最大电量单位数。
        internal const int MaxEyeScanBatteryCost = 1_000_000;
        // 配置中允许列出的机器档案数量上限。
        internal const int MaxMachineProfiles = 32;
        // 配置中允许列出的解锁材料 ID 数量上限。
        internal const int MaxUnlockMaterialIds = 32;
        // 义体额外行动次数配置允许的最大值。
        internal const int MaxExtraActions = 10;
        // 义体机器属性加成配置允许的最大基点数。
        internal const int MaxCyberneticMachineBonusBp = 100_000;
        // 义体暴露阈值偏移配置允许的最大点数。
        internal const int MaxExposureThresholdShift = 100;
        // 拟态皮肤每日暴露减免配置允许的最大点数。
        internal const int MaxSkinExposureReduction = 10_000;
        // 配置中允许声明的解锁配方条目数上限。
        internal const int MaxUnlockRecipes = 14;
        // 单条解锁配方允许的不同材料种类数上限。
        internal const int MaxRecipeIngredients = 8;
        // 配置中单种配方材料数量允许的最大件数。
        internal const int MaxIngredientUnits = 999;
        // 解锁交易中材料记录数和单项扣除数量的校验上限。
        internal const int MaxRecipeMaterialUnits = 64;
        // 外部配置 JSON 允许的最大嵌套深度。
        internal const int ConfigJsonDepth = 16;
        // 机械飞升周目 JSON 允许的最大嵌套深度。
        internal const int RunJsonDepth = 32;
        // 原版存档 JSON 读回时允许的最大嵌套深度。
        internal const int NativeSaveJsonDepth = 256;
    }
}
