using Nicokobo.ModUtilities;
namespace PsModI18n
{
    internal static class I18n
    {
        internal static string Get(string key, params object[] args) =>
            ModText.Get(Nicokobo.AutomatedLogistics.Integration.Content.Owner, "logistics." + key, args);

    }
}
