using Nicokobo.ModUtilities;
using DefinitelyStolen.Domain;

namespace DefinitelyStolen.Integration;

internal static class EscapeSmoke
{
    internal static GameItem Create()
    {
        var item = ItemDirectory.CreateEmptyItem(EscapeSmokeRules.ItemId);
        try
        {
            item.identifierName = EscapeSmokeRules.ItemId;
            item.name = ModText.Get(Identity.Owner, EscapeSmokeRules.ItemId + ".name");
            item.shortDescription = Native.Localize("carry_outside_automatically_consumed_when_theft_is");
            item.flavorText = Native.Localize("leave_a_cloud_between_you_and_trouble");
            item.SetSpriteAndShapeFromMod(Identity.Owner, "escape-smoke");
            item.SetShape(new GridShapeBuilder().SetData("11", 1).Build()); item.SetGameItemType("TOOL");
            item.unitBaseValue = 0; item.unitValue = EscapeSmokeRules.Value; item.mayStackItemFunc = (_, _) => false;
            item.forceDisableUse = item.forceDisableActivate = true; ModHelper.SetSource(item, Identity.Owner, "Definitely Stolen");
            return item;
        }
        catch { item.Destroy(); throw; }
    }
}
