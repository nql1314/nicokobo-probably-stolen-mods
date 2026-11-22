using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
namespace AugMechanicalAscension.Integration;
internal sealed partial class StoryRuntimeFlow
{
    private bool _letterArmed;
    private static void AfterSave(PlayerStore __instance, bool __runOriginal)
    {
        if (__runOriginal && _active is { _letterArmed: true } flow && ReferenceEquals(flow._store, __instance)) flow.WriteLetter();
    }
    private void WriteLetter()
    {
        if (!_letterArmed || !Ready || _state?.EndingId != "beyond_the_world" || _json == null) return;
        try
        {
            string name = StoryLetter.FileName();
            if (!ModHelper.ModDataFileExists(Owner, name))
            {
                string text = StoryLetter.Text(LetterName(), null);
                ModHelper.WriteModDataFileText(Owner, name, text);
            }
            _letterArmed = false;
            _message = AugText.Get("aug.the_letter_is_kept_in_this_mod");
        }
        catch (Exception error) { log("[Story] Letter: " + error.Message); _message = AugText.Get("aug.the_letter_didn_t_save_the_ending"); }
    }
    private string LetterName() => StoryLetter.ChooseName(Environment.UserName);
    internal bool UpdateItemInput() { StoryJournalWindow.Update(); return UpdateMagnifierInput(); }
    internal bool DrawItemUi() => DrawMagnifier();
}
