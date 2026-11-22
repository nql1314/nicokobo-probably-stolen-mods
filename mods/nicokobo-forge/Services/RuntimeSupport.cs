using System.Runtime.CompilerServices;
namespace Nicokobo.ContentInterop
{
    internal static class NativeObject
    {
        private sealed class Identity { internal readonly IntPtr Id = new(Interlocked.Increment(ref _next)); }
        private static long _next;
        private static readonly ConditionalWeakTable<object, Identity> Identities = new();
        internal static IntPtr Handle(object? value) => value == null || value is UnityEngine.Object obj && obj == null
            ? IntPtr.Zero : Identities.GetValue(value, _ => new()).Id;
    }
    internal static class ManagedIdentity
    {
        internal static IntPtr Handle(this object? value) => NativeObject.Handle(value);
        internal static T? TryCast<T>(this object? value) where T : class => value as T;
        internal static T Cast<T>(this object value) => (T)value;
    }
    internal static class NativeDelegate
    { internal static T Convert<T>(Delegate callback) where T : Delegate => callback is T typed ? typed : (T)Delegate.CreateDelegate(typeof(T), callback.Target, callback.Method); }
    internal sealed class CallbackLease(Action dispose) : IDisposable
    { private Action? _dispose = dispose; public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke(); }
}
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
