using static PsModI18n.I18n;
using UnityEngine;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class TerminalWindow
{
    private bool _powerStatisticsPage, _waterStatisticsPage;
    private float _nextStatisticsRead;
    private long _shownPower = -1;
    private long[] _shownWater = [];
    private (int Uid, string Id, int Count, decimal? Value, int Container)[] _shownInventory = [];
    private void SelectPowerStatisticsPage()
    {
        LeaveNetworkPage(); _statisticsPage = false;
        _powerStatisticsPage = true; _dirty = true;
    }
    private void SelectWaterStatisticsPage()
    {
        LeaveNetworkPage(); _statisticsPage = false;
        _waterStatisticsPage = true; _dirty = true;
    }
    private void RefreshStatisticsView()
    {
        if (!_dirty && Time.unscaledTime < _nextStatisticsRead) return;
        _nextStatisticsRead = Time.unscaledTime + .25f;
        if (_statisticsPage)
        {
            // Manual moves do not pass the automation methods. Poll only the
            // visible statistics page, sharing its existing bounded read rate.
            var items = runtime.ResourceContents().Select(view => (Uid: view.Item.GetUniqueID(),
                Id: view.Item.identifier, Count: view.Item.unitCount, Value: ValueOf(view.Item), Container: view.ContainerId))
                .OrderBy(item => item.Uid).ToArray();
            if (!items.SequenceEqual(_shownInventory)) _dirty = true;
            _shownInventory = items;
        }
        if (_powerStatisticsPage && runtime.State.Power.Unlocked)
        {
            long total = runtime.PowerTotal();
            if (total != _shownPower) _dirty = true;
            _shownPower = total;
        }
        if (_waterStatisticsPage && runtime.State.WaterUnlocked)
        {
            var totals = runtime.WaterTotals();
            if (!totals.SequenceEqual(_shownWater)) _dirty = true;
            _shownWater = totals;
        }
    }
    private void RenderPowerStatistics()
    {
        if (!runtime.State.Power.Unlocked)
        {
            string gridName = Content.LocalizedName(Content.PowerDevice)!;
            string message = AutomatedLogisticsRuntime.HasReactorPerk()
                ? PsModI18n.I18n.Get("starting_void_grid_pending", gridName)
                : PsModI18n.I18n.Get("unlock_statistics.power",
                    LocHelper.GetLocalizedNetworkUpgrade("network_" + AutomatedLogisticsNumbers.CommercialPowerUpgrade + "_title"), gridName);
            Text(message, 0, 70, 940, 160, BodySize);
            return;
        }
        Text(PsModI18n.I18n.Get("void_grid_power_statistics"), -240, 197, 500, 34, HeadingSize);
        Text(PsModI18n.I18n.Get("total_grid_energy"), -240, 152, 470, 28, BodySize);
        Text($"{runtime.PowerTotal()} / {runtime.PowerCapacity()} kJ", -240, 112, 470, 38, TitleSize);
        int yesterday = StoreStation.GetDayCounter() - 1;
        var last = runtime.State.Power.Totals.Days.LastOrDefault(day => day.Day == yesterday);
        Text(PsModI18n.I18n.Get("yesterday_s_energy_change"), 250, 152, 470, 28, BodySize);
        Text(last == null ? "—" : Signed(last.Change, false), 250, 112, 470, 38, TitleSize);
        RenderTotalHistory(runtime.State.Power.Totals, false);
    }
    private void RenderWaterStatistics()
    {
        if (!runtime.State.WaterUnlocked)
        {
            Text(PsModI18n.I18n.Get("unlock_statistics.water",
                LocHelper.GetLocalizedNetworkUpgrade("network_" + AutomatedLogisticsNumbers.WaterUpgrade + "_title"),
                Content.LocalizedName(Content.WaterDevice)!), 0, 70, 940, 160, BodySize);
            return;
        }
        var totals = runtime.WaterTotals();
        Text(PsModI18n.I18n.Get("water_statistics_total") + Number(totals.Skip(1).Sum() / 1_000_000m) + " L", -110, 197, 760, 34, HeadingSize);
        string[] names = [LocHelper.GetLocalizedMechanic("mech_condition_basewater"),
            LocHelper.GetLocalizedMechanic("mech_condition_high_quality_water"),
            LocHelper.GetLocalizedMechanic("mech_condition_pure_water")];
        Color[] colors = [Rgb(141, 117, 66), Rgb(56, 123, 118), Rgb(66, 105, 166)];
        for (int quality = 1; quality <= 3; quality++)
        {
            float x = -326 + (quality - 1) * 326;
            Text(names[quality - 1], x, 152, 310, 28, BodySize);
            Text(Number(totals[quality] / 1_000_000m) + " L", x, 112, 310, 38, TitleSize);
        }
        Rule(0, 72, 984);
        Text(PsModI18n.I18n.Get("last_10_days_water_changes_by_purity"), -240, 43, 500, 28, HeadingSize);
        for (int quality = 0; quality < 3; quality++)
        {
            Fill(Node("WaterLegend", _body!.transform, 125 + quality * 126, 43, 12, 12), colors[quality]);
            Text(names[quality], 177 + quality * 126, 43, 88, 24, CaptionSize);
        }
        var histories = runtime.State.WaterHistory;
        var days = histories.SelectMany(history => history.Days).Select(day => day.Day).Distinct().OrderBy(day => day).TakeLast(10).ToArray();
        double maximum = Math.Max(1, histories.SelectMany(history => history.Days).Select(day => Math.Abs((double)day.Change)).DefaultIfEmpty(0).Max());
        const float middle = -100, heightLimit = 100;
        for (int tick = -1; tick <= 1; tick++)
        {
            float y = middle + tick * heightLimit; Rule(38, y, 876);
            Text(Number((decimal)(maximum * tick) / 1_000_000m), -451, y, 84, 22, CaptionSize);
        }
        for (int index = 0; index < 10; index++)
        {
            float x = -356 + index * 87; int record = index - (10 - days.Length);
            Text(record < 0 ? "—" : PsModI18n.I18n.Get("day", days[record]), x, -238, 82, 24, CaptionSize);
            if (record < 0) continue;
            for (int quality = 0; quality < 3; quality++)
            {
                var day = histories[quality].Days.FirstOrDefault(entry => entry.Day == days[record]);
                if (day == null) continue;
                float height = (float)(Math.Abs((double)day.Change) / maximum * heightLimit);
                if (height > 0) Fill(Node("WaterChange", _body!.transform, x + (quality - 1) * 21,
                    middle + (day.Change >= 0 ? height / 2 : -height / 2), 17, height), colors[quality]);
            }
        }
        if (days.Length == 0) Text(PsModI18n.I18n.Get("water_changes_by_purity_appear_after_the"), 0, -65, 620, 30, BodySize);
        Text(PsModI18n.I18n.Get("void_tank_only_in_l_morning_to"), 0, -277, 980, 26, CaptionSize);
    }
    private static string Signed(long value, bool water) => (value > 0 ? "+" : "") +
        (water ? Number(value / 1_000_000m) + " L" : value.ToString());
    private void RenderTotalHistory(TotalHistory history, bool water)
    {
        Rule(0, 72, 984);
        Text(PsModI18n.I18n.Get("last_10_days_daily_total_change"), -220, 43, 540, 28, HeadingSize);
        const float middle = -100, chartHeight = 100;
        double maximum = Math.Max(1, history.Days.Select(day => Math.Abs((double)day.Change)).DefaultIfEmpty(0).Max());
        Rule(38, middle, 876);
        Text("0", -450, middle, 84, 22, CaptionSize);
        for (int index = 0; index < 10; index++)
        {
            float x = -356 + index * 87;
            int recordIndex = index - (10 - history.Days.Count);
            var day = recordIndex >= 0 ? history.Days[recordIndex] : null;
            Text(day == null ? "—" : PsModI18n.I18n.Get("day", day.Day), x, -238, 82, 24, CaptionSize);
            if (day == null) continue;
            float height = (float)(Math.Abs((double)day.Change) / maximum * chartHeight);
            bool positive = day.Change >= 0;
            if (height > 0) Fill(Node("TotalChange", _body!.transform, x, middle + (positive ? height / 2 : -height / 2), 32, height),
                positive ? Rgb(56, 123, 118) : Rgb(185, 84, 43));
            Text(Signed(day.Change, water), x, positive ? middle + height + 14 : middle - height - 14, 86, 24, CaptionSize);
        }
        if (history.Days.Count == 0) Text(PsModI18n.I18n.Get("daily_changes_appear_after_the_first_night"), 0, -65, 620, 30, BodySize);
        Text(water ? PsModI18n.I18n.Get("morning_to_morning_water_changes_are_recorded") :
            PsModI18n.I18n.Get("morning_to_morning_grid_changes_are_recorded"), 0, -277, 980, 26, CaptionSize);
    }
}
