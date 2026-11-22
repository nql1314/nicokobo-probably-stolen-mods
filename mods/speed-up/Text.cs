using Nicokobo.ModUtilities;
namespace SpeedUp
{
    internal static class Text
    {
        internal static string Get(string key, params object[] args) =>
            ModText.Get(SpeedUpMod.Id, "speed." + key, args);
    }
}
