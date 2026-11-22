public static class IndustrialFishFeedHelper
{
    public const int KCAL_PER_CREDIT = 50;
    public const long ReturnedValue = 777;
    public static readonly List<int> Requests = new();
    public static void SetKcal(GameItem item, int kcal)
    {
        Requests.Add(kcal);
        item.GetTagReadonly("CALORIE_VALUE_TAG").SetInt(kcal);
        item.unitValue = ReturnedValue;
    }
}
