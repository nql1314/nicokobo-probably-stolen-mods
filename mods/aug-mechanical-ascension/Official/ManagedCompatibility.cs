using System.Runtime.CompilerServices;
using UnityEngine;

namespace AugMechanicalAscension.Official
{
    // Stable managed identity only. This is never an address, GC handle, or native call.
    internal static class NativeObject
    {
        private sealed class Identity { internal readonly IntPtr Id = new(Interlocked.Increment(ref _next)); }
        private static long _next;
        private static readonly ConditionalWeakTable<object, Identity> Identities = new();
        internal static IntPtr Handle(object? value) => value == null || value is UnityEngine.Object obj && obj == null
            ? IntPtr.Zero : Identities.GetValue(value, _ => new()).Id;
    }
    internal static class HarmonyCompatibility
    {
        internal static void UnpatchSelf(this HarmonyLib.Harmony harmony) => harmony.UnpatchAll(harmony.Id);
    }
    internal static class NativeFields
    {
        private static readonly Dictionary<(Type,string),System.Reflection.FieldInfo> Fields=new();
        private static System.Reflection.FieldInfo Field(object target,string name)
        {
            // These are the only private fields used by the shipped adapters.
            // Reject any other receiver before performing reflection.
            var type = target switch
            {
                BargainUIManager => typeof(BargainUIManager),
                ItemContextHandler => typeof(ItemContextHandler),
                RichTextElement => typeof(RichTextElement),
                ItemFeature => typeof(ItemFeature),
                _ => throw new ArgumentException("Unsupported native field receiver",nameof(target))
            };
            var key=(type,name);
            if(Fields.TryGetValue(key,out var found))return found;
            found=target switch
            {
                BargainUIManager => HarmonyLib.AccessTools.Field(typeof(BargainUIManager),name),
                ItemContextHandler => HarmonyLib.AccessTools.Field(typeof(ItemContextHandler),name),
                RichTextElement => HarmonyLib.AccessTools.Field(typeof(RichTextElement),name),
                ItemFeature => HarmonyLib.AccessTools.Field(typeof(ItemFeature),name),
                _ => throw new ArgumentException("Unsupported native field receiver",nameof(target))
            } ?? throw new MissingFieldException(type.FullName,name);
            if(found.DeclaringType?.Assembly!=typeof(PlayerStore).Assembly)throw new InvalidOperationException("Only game fields may be accessed");
            Fields.Add(key,found);return found;
        }
        internal static T Get<T>(object target,string name)=>(T)Field(target,name).GetValue(target)!;
    }
#if NETSTANDARD2_1
    internal static class EnumerableCompatibility
    {
        internal static IEnumerable<(TFirst First,TSecond Second)> Zip<TFirst,TSecond>(this IEnumerable<TFirst> first,IEnumerable<TSecond> second)=>first.Zip(second,(a,b)=>(a,b));
    }
#endif
    internal static class ManagedRandom { internal static readonly System.Random Shared=new(); }
    internal static class OfficialHookApi
    {
        internal static bool TryValidateNativeTarget(System.Reflection.MethodBase method, out string reason)
        {
            bool valid = method.DeclaringType?.Assembly == typeof(PlayerStore).Assembly && !method.IsAbstract && !method.ContainsGenericParameters;
            reason = valid ? "managed game method" : "Target must be a concrete method on the game assembly";
            return valid;
        }
    }
    internal sealed class CallbackLease(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
