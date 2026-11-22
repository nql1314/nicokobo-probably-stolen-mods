using AugMechanicalAscension;
using MechcoreProtocol.ModuleMatrix;
using MechcoreProtocol.SynthesisExpansion;
using Nicokobo.ContentInterop;

namespace MechcoreProtocol;

public sealed class MechcoreMod : IMod
{
    private readonly SynthesisMod _synthesis = new();
    private readonly MatrixMod _matrix = new();
    private readonly AugMod _aug = new();

    public void Init(ModManifest manifest)
    {
        _synthesis.Init(manifest);
        _matrix.Init(manifest);
        _aug.Init(manifest);
    }

    public void OnEnable() => ForgeClient.Enable(MechcorePackage.Id, Start, Stop, Assets);
    public void OnDisable() => ForgeClient.Disable(Stop);

    private void Start()
    {
        var previous = ModLoader.Mods.Where(mod => mod.IsEnabled && MechcorePackage.IsPreviousPackage(mod.Manifest.ID))
            .Select(mod => mod.Manifest.ID).ToArray();
        if (previous.Length != 0)
            throw new InvalidOperationException("Mechcore Protocol replaces these packages; disable them before enabling the combined mod: " + string.Join(", ", previous));
        _synthesis.EnableContent();
        _matrix.EnableContent();
        _aug.EnableContent();
    }

    private void Stop()
    {
        // Clean up every component even if one partially initialized or fails to stop.
        try { _aug.DisableContent(); }
        finally
        {
            try { _matrix.DisableContent(); }
            finally { _synthesis.DisableContent(); }
        }
    }

    private static void Assets(LoadedMod mod)
    {
        SynthesisMod.AssetsLoaded(mod);
        MatrixMod.Assets(mod);
        AugMod.AssetsLoaded(mod);
    }
}
