using AugMechanicalAscension.Official;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Integration;

internal static class StoryCoreRegistration
{
    internal static bool Install(Action<string> log)
    {
        StoryIconAtlas.Configure(log);
        var result = OfficialItemApi.RegisterItem("nicokobo.aug", StoryState.CoreId, Create,
            new NativeItemOptions(Name: new("aug.distress_core_2"),
                ShortDescription: new("aug.the_light_is_still_on_sending_the"),
                FlavorText: new("aug.is_anyone_there_please_don_t_send")));
        log($"[Story] Distress core registration={result.Status}; {result.Reason}");
        return result.Status is SubmitStatus.Accepted or SubmitStatus.AlreadyPresent;
    }

    private static GameItem Create()
    {
        var item = DirectoryMaster.Create<GameItem>("common_electronic");
        if (item == null) throw new InvalidOperationException("Electronic template unavailable");
        try
        {
            item.identifier = StoryState.CoreId;
            item.name = AugText.Get("aug.distress_core_2");
            item.shortDescription = AugText.Get("aug.the_light_is_still_on_sending_the");
            item.flavorText = AugText.Get("aug.is_anyone_there_please_don_t_send");
            item.SetShape(new GridShapeBuilder().SetData("1111", 2).Build());
            StoryIconAtlas.AssignOrStage(item, "distress_core");
            item.RemoveAllGameItemType();
            item.mayStackItemFunc = null;
            item.mayActivateSlotItemFunc = null;
            item.canActivateSlotItemFunc = null;
            item.onActivateSlotItemFunc = null;
            item.onLoaded = null;
            item.forceDisableUse = true;
            item.forceDisableActivate = true;
            item.activateDefault = false;
            item.unitBaseValue = 0;
            item.unitValue = 200;
            return item;
        }
        catch { if (item.parentInventory == null) item.Destroy(); throw; }
    }
}
