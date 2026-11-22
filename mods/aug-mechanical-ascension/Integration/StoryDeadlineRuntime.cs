using AugMechanicalAscension.Official;
using AugMechanicalAscension.Domain;
using HarmonyLib;

namespace AugMechanicalAscension.Integration;

internal sealed partial class StoryRuntimeFlow
{
    private void InstallDeadlineHooks()
    {
        var guard = new HarmonyLib.Harmony("nicokobo.aug.story.deadline");
        try
        {
            foreach (string method in new[] { nameof(PlayerStore.EndDay), nameof(PlayerStore.EndNight) })
                guard.Patch(AccessTools.Method(typeof(PlayerStore), method, Type.EmptyTypes)
                    ?? throw new MissingMethodException(nameof(PlayerStore), method),
                    prefix: new HarmonyMethod(typeof(StoryRuntimeFlow), nameof(BeforeDayAdvance)) { priority = Priority.First });
        }
        catch { guard.UnpatchAll(guard.Id); throw; }
    }
    private static bool BeforeDayAdvance(PlayerStore __instance)
    {
        var flow = _active;
        if (flow == null) return true;
        flow.Update();
        if (!flow.Ready || flow._store == null || NativeObject.Handle(flow._store) != NativeObject.Handle(__instance) ||
            flow._state == null || !StoryRules.DecisionDue(flow._state, StoreStation.GetDayCounter())) return true;
        flow.OpenDueDecision();
        return false;
    }
    private string DeadlineText()
    {
        if (_state == null || StoryRules.DecisionDeadline(_state) is not { } deadline) return "";
        long remaining = Math.Max(0, deadline - StoreStation.GetDayCounter());
        return remaining > 0
            ? AugText.Get("aug.reply_due_on_day_days_left_reopening", deadline, remaining)
            : AugText.Get("aug.the_deadline_is_today_answer_before_you");
    }
    private void OpenDueDecision()
    {
        if (!Ready || _state == null || !StoryRules.DecisionDue(_state, StoreStation.GetDayCounter())) return;
        _message = DeadlineText() + AugText.Get("aug.right_click_the_story_journal_to_reply");
        OpenRecord();
    }
    private string ApplyDueDecision(string action)
    {
        if (!Ready || _state == null || !StoryRules.DecisionDue(_state, StoreStation.GetDayCounter()))
            return AugText.Get("aug.this_deadline_encounter_is_complete_or_expired");
        string ev = _state.ActiveEventId!;
        return Apply(action) ? StoryReplies.Get(ev, action, null, _state) + "\n\n" + _message
            : AugText.Get("aug.i_am_still_waiting_for_your_decision") + "\n" + _message;
    }
}
