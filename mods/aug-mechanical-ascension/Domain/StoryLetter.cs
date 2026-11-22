namespace AugMechanicalAscension.Domain;

internal static class StoryLetter
{
    internal static string ChooseName(string? system) =>
        new string((system ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
    internal static string FileName(bool? en = null) => AugText.GetFor("aug.an_unaddressed_letter_txt", en);
    internal static string Text(string name, bool? en = null) => AugText.GetFor("aug.story.escape_letter", en, name);
}
