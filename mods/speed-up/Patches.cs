using Febucci.UI;
using HarmonyLib;

namespace SpeedUp
{
    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.GetTypewriterDelay))]
    internal static class GetTypewriterDelayPatch
    {
        static void Postfix(ref float __result, bool __runOriginal)
        {
            if (__runOriginal) __result = SpeedUpSettings.ApplyTypewriterDelay(__result);
        }
    }

    [HarmonyPatch(typeof(TypewriterByCharacter), "GetWaitAppearanceTimeOf")]
    internal static class GetWaitAppearanceTimeOfPatch
    {
        static void Postfix(TypewriterByCharacter __instance, ref float __result, bool __runOriginal)
        {
            if (__runOriginal && DialogueTypewriters.Contains(__instance))
                __result = SpeedUpSettings.ApplyTypewriterDelay(__result);
        }
    }

    [HarmonyPatch(typeof(TypewriterByCharacter), "GetWaitDisappearanceTimeOf")]
    internal static class GetWaitDisappearanceTimeOfPatch
    {
        static void Postfix(TypewriterByCharacter __instance, ref float __result, bool __runOriginal)
        {
            if (__runOriginal && DialogueTypewriters.Contains(__instance))
                __result = SpeedUpSettings.ApplyTypewriterDelay(__result);
        }
    }

    internal static class DialogueTypewriters
    {
        internal static bool Contains(TypewriterByCharacter typewriter)
        {
            if (typewriter == null) return false;
            var shop = DialogUIManager.Instance;
            var phone = DialoguePhoneUIManager.Instance;
            return shop != null && (Matches(shop.clientBubble, typewriter) || Matches(shop.playerBubble, typewriter)
                    || Matches(shop.secBubble, typewriter))
                || phone != null && (Matches(phone.clientBubble, typewriter) || Matches(phone.playerBubble, typewriter));
        }

        static bool Matches(TextBubble bubble, TypewriterByCharacter typewriter) => bubble != null && bubble.typewriter == typewriter;
    }

    [HarmonyPatch(typeof(DialogUIManager), "ScheduleFlow")]
    internal static class DialogUiScheduleFlowPatch
    {
        static void Prefix(ref float delay) => delay = SpeedUpSettings.ApplyQueueDelay(delay);
    }

    [HarmonyPatch(typeof(DialoguePhoneUIManager), "ScheduleFlow")]
    internal static class PhoneDialogScheduleFlowPatch
    {
        static void Prefix(ref float delay) => delay = SpeedUpSettings.ApplyQueueDelay(delay);
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.ContinueConversation))]
    internal static class DialogUiContinueConversationPatch
    {
        static void Prefix(DialogUIManager __instance, ref Dialogue ___displayingUncompletedDialog)
        {
            Dialogue line = __instance.currentDialog;
            if (SpeedUpSettings.DialogueAccelerated && ___displayingUncompletedDialog == null
                && line != null && line.endAction != null && !line.isEndActionExecuted)
            {
                // Keep the native visitor guard and ExecuteEndAction's exactly-once protection.
                // Accelerated flow can reach the next line before the delayed end action fires.
                ___displayingUncompletedDialog = line;
            }
        }
    }

    [HarmonyPatch(typeof(DialoguePhoneUIManager), nameof(DialoguePhoneUIManager.ContinueConversation))]
    internal static class PhoneDialogContinueConversationPatch
    {
        static void Prefix(DialoguePhoneUIManager __instance, ref Dialogue ___displayingUncompletedDialog)
        {
            Dialogue line = __instance.currentDialog;
            if (SpeedUpSettings.DialogueAccelerated && ___displayingUncompletedDialog == null
                && line != null && line.endAction != null && !line.isEndActionExecuted)
                ___displayingUncompletedDialog = line;
        }
    }
}
