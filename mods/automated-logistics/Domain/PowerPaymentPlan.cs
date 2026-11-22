namespace Nicokobo.AutomatedLogistics.Domain;

internal enum PowerPaymentSource { None, Local, Grid }
internal static class PowerPaymentPlan
{
    // One source pays the whole operation. Capacity remains a property of the
    // installed battery, and the reserve threshold is tested before the draw.
    internal static PowerPaymentSource Select(int local, int capacity, long grid, int cost)
    {
        if (local < 0 || capacity <= 0 || grid < 0 || cost < 0 || cost > capacity) return PowerPaymentSource.None;
        if ((long)local * 4 > capacity && local >= cost) return PowerPaymentSource.Local;
        if (grid >= cost) return PowerPaymentSource.Grid;
        return local >= cost ? PowerPaymentSource.Local : PowerPaymentSource.None;
    }
}
