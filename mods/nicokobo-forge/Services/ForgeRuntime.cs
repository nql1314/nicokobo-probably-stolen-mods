using UnityEngine;
namespace Nicokobo.ContentInterop;

internal static class ForgeRuntime
{
    private static GameObject? _host;
    private static bool _ready;
    internal static void Attach()
    {
        NightShop.Attach(); ManufacturingTerminal.Attach();
        ContentRuntime.Detaching += Remove;
        ModHook.OnModItemDirectoryInit += OfficialWorkshopApi.RegisterCard;
        ModHook.OnGameLoadedInit += NativeProductionOrder.Reset;
        ModItemHitAreaPatch.Enable();
    }
    internal static void Ready()
    {
        if (_ready) return;
        try
        {
            OfficialStoryPresentation.Install(Debug.LogWarning);
            PresentationInputGuard.Install();
            OfficialWorkshopApi.RegisterPhone();
            NativeWorkshop.Install();
            _host = new GameObject("NicoKobo Forge Services");
            UnityEngine.Object.DontDestroyOnLoad(_host); _host.AddComponent<ForgeServicesDriver>();
            _ready = true;
        }
        catch { Shutdown(); throw; }
    }
    internal static void Remove(string owner)
    {
        NativeMachines.Remove(owner); NativeInventoryAdmissionApi.Remove(owner); PowerConsumers.Remove(owner);
        OfficialWorkshopApi.Remove(owner); OfficialStoryUi.CloseOwner(owner);
        OfficialStoryPresentation.Remove(owner); OfficialStoryPhoneRuntime.Remove(owner);
        Nicokobo.Forge.Input.TextInputFocus.Unregister(owner);
    }
    internal static void Shutdown()
    {
        ModItemHitAreaPatch.Disable();
        ModHook.OnModItemDirectoryInit -= OfficialWorkshopApi.RegisterCard;
        ModHook.OnGameLoadedInit -= NativeProductionOrder.Reset;
        NativeProductionOrder.Reset();
        ContentRuntime.Detaching -= Remove;
        ManufacturingTerminal.Detach(); NightShop.Detach(); NativeWorkshop.Uninstall(); OfficialWorkshopApi.Clear();
        PresentationInputGuard.Uninstall(); OfficialStoryPresentation.Uninstall(); NativeLiquidValueRuntime.Uninstall();
        if (_host != null) UnityEngine.Object.Destroy(_host);
        _host = null; _ready = false;
    }
    internal static void Tick() { if (!_ready) return; OfficialStoryPresentation.Update(); NativeWorkshop.Update(); OfficialWorkshopApi.Update(); }
}
internal sealed class ForgeServicesDriver : MonoBehaviour
{
    private void Update() => ForgeRuntime.Tick();
}
