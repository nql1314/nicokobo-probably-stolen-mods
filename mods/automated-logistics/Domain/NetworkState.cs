namespace Nicokobo.AutomatedLogistics.Domain;

public sealed class ResourceStorageState
{
    public int Rows { get; set; } = AutomatedLogisticsNumbers.ResourceInitialRows;
    public int WaterCapacityParts { get; set; } = AutomatedLogisticsNumbers.WaterInitialParts;
    public int LastUpgradeItemId { get; set; }
    public bool Valid() => Rows > 0 && Rows <= int.MaxValue / AutomatedLogisticsNumbers.ResourceWidth &&
        WaterCapacityParts > 0 && LastUpgradeItemId >= 0;
    public static int AddedRows(int cells) => cells > 0 ? checked((int)(((long)cells + AutomatedLogisticsNumbers.ResourceWidth - 1) / AutomatedLogisticsNumbers.ResourceWidth)) : 0;
}

public sealed class PowerNetworkState
{
    public long StoredEnergy { get; set; }
    public TotalHistory Totals { get; set; } = new();
    public int InstanceId { get; set; }
    public long Consumed { get; set; }
    public long Generated { get; set; }
    public long Transferred { get; set; }
    public int AquacultureDay { get; set; } = -1;
    public HashSet<int> AquacultureGenerators { get; set; } = new();
    public int HypertechDay { get; set; } = -1;
    public HashSet<int> HypertechGenerators { get; set; } = new();
    public long SettledConsumed { get; set; }
    public long SettledGenerated { get; set; }
    public List<PowerDayRecord> Days { get; set; } = new();
    public long AddHypertechGeneration(int day, int instanceId, int output, long capacity)
    {
        if (day < HypertechDay) return 0;
        if (day != HypertechDay) { HypertechDay = day; HypertechGenerators.Clear(); }
        if (!HypertechGenerators.Add(instanceId)) return 0;
        // A full grid still consumes this day's generation; overflow is not banked.
        long added = Math.Min(output, Math.Max(0, capacity - StoredEnergy));
        StoredEnergy = checked(StoredEnergy + added);
        Generated = checked(Generated + added);
        return added;
    }
    public bool SettleDay(int day)
    {
        if (day < 0 || Days.Any(entry => entry.Day >= day)) return false;
        Days.Add(new() { Day = day, Consumed = Consumed - SettledConsumed, Generated = Generated - SettledGenerated });
        if (Days.Count > 10) Days.RemoveRange(0, Days.Count - 10);
        SettledConsumed = Consumed; SettledGenerated = Generated;
        return true;
    }
    public bool Unlocked { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Valid() => StoredEnergy >= 0 && Totals != null && Totals.Valid() && Days != null && Days.Count <= 10 &&
        AquacultureDay >= -1 && AquacultureGenerators != null && AquacultureGenerators.All(id => id > 0) &&
        (AquacultureDay >= 0 || AquacultureGenerators.Count == 0) &&
        HypertechDay >= -1 && HypertechGenerators != null && HypertechGenerators.All(id => id > 0) &&
        (HypertechDay >= 0 || HypertechGenerators.Count == 0) &&
        Days.All(day => day != null && day.Day >= 0 && day.Consumed >= 0 && day.Generated >= 0) &&
        Days.Zip(Days.Skip(1), (left, right) => left.Day < right.Day).All(ordered => ordered) &&
        SettledConsumed >= 0 && SettledConsumed <= Consumed && SettledGenerated >= 0 && SettledGenerated <= Generated && Consumed >= 0 && Generated >= 0 && Transferred >= 0 && InstanceId >= 0 && Width >= 0 && Height >= 0 && (!Unlocked || InstanceId > 0 && Width > 0 && Height > 0);
}

public sealed class PowerDayRecord
{
    public int Day { get; set; }
    public long Consumed { get; set; }
    public long Generated { get; set; }
}

public sealed class TotalDayRecord
{
    public int Day { get; set; }
    public long Total { get; set; }
    public long Change { get; set; }
}
public sealed class TotalHistory
{
    public long? Baseline { get; set; }
    public List<TotalDayRecord> Days { get; set; } = new();
    public bool Settle(int day, long total)
    {
        if (day < 0 || total < 0 || Days.Any(entry => entry.Day >= day)) return false;
        Days.Add(new() { Day = day, Total = total, Change = total - (Baseline ?? total) });
        if (Days.Count > 10) Days.RemoveRange(0, Days.Count - 10);
        Baseline = total;
        return true;
    }
    public bool Valid() => !(Baseline < 0) && Days != null && Days.Count <= 10 &&
        Days.All(entry => entry != null && entry.Day >= 0 && entry.Total >= 0) &&
        Days.Zip(Days.Skip(1), (a, b) => a.Day < b.Day).All(value => value);
}
