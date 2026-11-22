namespace Nicokobo.ContentInterop;

// The existing gameplay harnesses compile the real Forge engine in-process.
// Only native loader activation is replaced here; the separate-assembly Forge
// harness exercises the actual client discovery, handshake and deferred start.
public static class ForgeClient
{
    public static void Enable(string owner, Action start, Action stop, Action<LoadedMod>? assets = null) => start();
    public static void Disable(Action stop) => stop();
}
