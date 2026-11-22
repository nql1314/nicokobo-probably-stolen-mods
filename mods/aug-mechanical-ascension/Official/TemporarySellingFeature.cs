namespace ProbablyStolen.ModSupport;

// Content owns the ID and eligibility. Reuse an unchanged native feature and
// rebuild it after native sale-panel reset or a changed customer/effect.
internal static class TemporarySellingFeature
{
    internal static void Remove(GameItem item, string id)
    {
        if (item.FindItemFeatureByID(id) != null) item.RemoveItemFeatureByID(id);
    }

    internal static void Ensure(GameItem item, string id, int modifier, string display, bool enabled)
    {
        var existing = item.FindItemFeatureByID(id);
        if (!enabled)
        {
            if (existing != null) item.RemoveItemFeatureByID(id);
            return;
        }
        if (existing != null && NativeObject.Handle(existing) != IntPtr.Zero &&
            existing.featureType == ItemFeature.FeatureType.TemporarySelling &&
            existing.valueStage == ItemFeature.ValueStage.Market && existing.category == id &&
            existing.isFeatureMatch && existing.isExposable && !existing.isPublicHidden &&
            !existing.isDisabled && !existing.useCondition && !existing.IsExposed() &&
            existing.GetActualValueModifier() == modifier && existing.GetFakeValueModifier() == modifier &&
            // GetPublicDisplay formats the name with a colored price modifier.
            // Compare the stored text, otherwise every value read rebuilds it.
            NativeFields.Get<string>(existing,"publicDisplay") == display && NativeFields.Get<string>(existing,"actualDisplay") == display && Unique(item, id))
            return;
        if (existing != null) item.RemoveItemFeatureByID(id);
        var feature = new ItemFeature(ItemFeature.FeatureType.TemporarySelling);
        feature.identifier = id;
        feature.category = id;
        feature.valueStage = ItemFeature.ValueStage.Market;
        feature.isFeatureMatch = true;
        feature.isExposable = true;
        feature.isPublicHidden = false;
        feature.SetPublicDisplay(display);
        feature.SetActualDisplay(display);
        feature.SetValueModifier(modifier);
        feature.SetPreExposedValueMod(modifier, true);
        item.AddItemFeature(feature);
    }

    private static bool Unique(GameItem item, string id)
    {
        int count = 0;
        foreach (var feature in item.itemFeatures)
            if (feature?.identifier == id && ++count > 1) return false;
        return count == 1;
    }
}
