using UnityEngine;
namespace PsModInput
{
    internal static class TextInputFocus
    {
        internal static void Register(string owner, Func<bool> editing) => Nicokobo.ContentInterop.ForgeClient.Call("input.register", owner, editing);
        internal static void Unregister(string owner) { if (Nicokobo.ContentInterop.ForgeClient.Connected) Nicokobo.ContentInterop.ForgeClient.Call("input.remove", owner); }
        internal static void BlockCurrentFrame() => Nicokobo.ContentInterop.ForgeClient.Call("input.block");
        internal static bool ShouldSuppressHotkeys() => Nicokobo.ContentInterop.ForgeClient.Call<bool>("input.suppress");
    }
}
namespace ProbablyStolen.ModSupport
{
    internal static class ReadingWindowDrag
    {
        internal static bool ContainsPointer(GameObject? window) => Nicokobo.ContentInterop.ForgeClient.Call<bool>("window.pointer", window!);
        internal static void KeepCursor(GameObject? window) => Nicokobo.ContentInterop.ForgeClient.Call("window.cursor", window!);
        internal static void Attach(GameObject? window, float titleHeight = 96) => Nicokobo.ContentInterop.ForgeClient.Call("window.drag", window!, titleHeight);
        internal static void Clamp(RectTransform panel) => Nicokobo.ContentInterop.ForgeClient.Call("window.clamp", panel);
    }
}
