using System.Reflection;
using HarmonyLib;

namespace StockReroll
{
    internal static class StoreAccess
    {
        // Instance creates a run if none exists. Read the current game's field
        // without invoking that getter, including from dialogue observations.
        static readonly FieldInfo InstanceField = AccessTools.Field(typeof(PlayerStore), "instance");
        internal static PlayerStore Current => (PlayerStore)InstanceField.GetValue(null);
    }
}
