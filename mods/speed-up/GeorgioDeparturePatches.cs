using System;
using HarmonyLib;

namespace SpeedUp
{
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.DismissCurrentClient))]
    internal static class GeorgioTradeFarewellPatch
    {
        static void Prefix(PlayerStore __instance, out Dialogue __state)
        {
            StoreClient client = __instance.currentClientInstance?.GetClientBlueprint();
            __state = IsGeorgio(client) ? client.allDoneDialogue : null;
            while (__state?.nextDialogue != null)
                __state = __state.nextDialogue;
        }

        static void Postfix(Dialogue __state, bool __runOriginal)
        {
            // A queued farewell can still execute after its customer has been dismissed.
            if (__runOriginal && __state != null)
                __state.isEndActionExecuted = true;
        }

        internal static bool IsGeorgio(StoreClient client) =>
            client?.identifier?.StartsWith("georgio", StringComparison.Ordinal) == true;
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.CurrentClientLeave))]
    internal static class GeorgioDelayedDeparturePatch
    {
        static bool Prefix(float time)
        {
            if (time == 0f)
                return true;

            StoreClientInstance client = PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance : null;
            // InterruptDialog can run the farewell while the counter is already empty.
            if (client == null)
                return false;
            if (!GeorgioTradeFarewellPatch.IsGeorgio(client.GetClientBlueprint()))
                return true;

            ModHelper.StartCoroutine(SpeedUpMod.Id, MetaHelper.WaitAndDo(time, () =>
            {
                if (PlayerStore.HasInstance && ReferenceEquals(PlayerStore.Instance.currentClientInstance, client))
                    PlayerStore.Instance.DismissCurrentClient(callNextWhenPossible: false);
            }));
            return false;
        }
    }
}
