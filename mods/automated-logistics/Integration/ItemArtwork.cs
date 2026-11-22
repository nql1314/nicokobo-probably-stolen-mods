using UnityEngine;

namespace Nicokobo.AutomatedLogistics.Integration;

internal static class ItemArtwork
{
    private const string AtlasKey = "Nicokobo/AutomatedLogistics";
    private sealed record Icon(string ItemId, string Key, int Width, int Height);
    private static readonly Icon[] Icons =
    {
        new(Content.Feeder, "auto_feeder", 16, 16),
        new(Content.Ejector, "output_ejector", 16, 16),
        new(Content.Terminal, "network_terminal", 32, 48),
        new(Content.WaterDevice, "void_tank", 48, 48),
        new(Content.ResourceDevice, "void_container", 48, 48),
        new(Content.PowerDevice, "void_grid", 48, 48)
    };
    private static readonly NativeSpriteAtlas Atlas = NativeAssetsApi.CreateEmbeddedSpriteAtlas(
        Content.Owner, AtlasKey, typeof(ItemArtwork).Assembly, Icons.Select(icon => new NativeSpriteDefinition(
            icon.Key, "Nicokobo.AutomatedLogistics.Assets.Icons." + icon.Key + ".png", icon.Width, icon.Height)).ToArray());
    private static Action<string>? _log;

    internal static void Configure(Action<string> log) => _log = log;
    internal static void TryInstall() => Atlas.TryInstall(
        () => _log?.Invoke("自动化图标已载入：" + Atlas.Count + "/" + Icons.Length),
        ex => _log?.Invoke("自动化图标载入失败：" + ex.Message));

    internal static void Apply(GameItem item)
    {
        if (item.identifier == Content.WaterDevice) { ApplyWater(item); return; }
        var icon = Icons.FirstOrDefault(i => i.ItemId == item.identifier);
        if (icon == null) return;
        TryInstall();
        Atlas.Assign(item, icon.Key, allowStaging: true);
        // Sprite size and footprint are separate. Preserve the native tool
        // footprints; only the two automation modules change to one cell.
        if (item.identifier is Content.Feeder or Content.Ejector)
            item.SetShape(new GridShapeBuilder().SetData("1", 1).Build());
    }
    internal static void ApplyDisposableBattery(GameItem item)
    {
        if (item.identifier != "energy_credit_dis") return;
        int capacity = item.GetTagReadonly("power_source_item_max_energy").valueInt;
        if (capacity <= 0) return;
        long percent = (long)item.GetTagReadonly("power_source_item_energy").valueInt * 100 / capacity;
        int level = percent switch { >= 90 => 100, >= 70 => 80, >= 50 => 60, >= 30 => 40, >= 10 => 20, >= 1 => 10, _ => 0 };
        string sprite = "battery_dis" + level;
        // Build 26337366 ships these sprites but omits this cell from the native updater.
        if (item.spriteAtlasPath != "Items/items_tool" || item.spritePath != sprite)
            item.SetSpriteAndShape("Items/items_tool", sprite);
    }
    private const string WaterAtlasKey = AtlasKey + "/WaterLevels";
    private static Texture2D? _waterTemplate;
    private static readonly Dictionary<(int Level, int Quality), (Texture2D Texture, Sprite Sprite)> WaterLevels = new();
    internal static void ApplyWater(GameItem item)
    {
        TryInstall();
        if (!Atlas.Installed) { Atlas.Assign(item, "void_tank", allowStaging: true); return; }
        var liquid = NativeLiquidApi.Capture(item);
        int level = liquid is { CapacityParts: > 0, TotalParts: > 0 }
            ? (int)Math.Clamp(((long)liquid.TotalParts * 4 + liquid.CapacityParts - 1) / liquid.CapacityParts, 1, 4) : 0;
        int quality = level == 0 ? 0 : Math.Clamp(NativeLiquidApi.WaterQuality(item), 0, 3);
        item.getItemSpriteModifierForItemFunc = null;
        if (!WaterLevels.TryGetValue((level, quality), out var entry))
        {
            if (_waterTemplate == null)
            {
                using var stream = typeof(ItemArtwork).Assembly.GetManifestResourceStream("Nicokobo.AutomatedLogistics.Assets.Icons.void_tank.png")
                    ?? throw new InvalidOperationException("虚空水罐图标缺失");
                using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                _waterTemplate = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                _waterTemplate.hideFlags = HideFlags.HideAndDontSave;
                if (!ImageConversion.LoadImage(_waterTemplate, bytes.ToArray(), false))
                    throw new InvalidOperationException("虚空水罐图标读取失败");
            }
            var original = _waterTemplate;
            var texture = new Texture2D(48, 48, TextureFormat.RGBA32, false, false);
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixels(original.GetPixels());
            // Preserve the original shell. Only the glass viewport changes:
            // five fill bands, water-quality tint, depth shading and highlights.
            var water = quality switch
            {
                3 => new Color(.30f, .47f, .56f, 1), // pure: clear slate blue
                2 => new Color(.22f, .37f, .43f, 1), // good: muted blue
                1 => new Color(.29f, .35f, .28f, 1), // basic: grey-green
                _ => new Color(.34f, .29f, .23f, 1)  // mixed/unknown: cloudy brown
            };
            int rows = level * 4;
            for (int y = 13; y < 13 + rows; y++)
                for (int x = 12; x <= 35; x++)
                {
                    if (y == 13 && (x == 12 || x == 35)) continue;
                    float depth = (y - 13) / 16f;
                    var shade = Color.Lerp(water * .65f, water, .35f + depth * .65f); shade.a = 1;
                    if (x is 13 or 14) shade = Color.Lerp(shade, new Color(.64f, .69f, .69f, 1), x == 13 ? .27f : .10f);
                    if (y == 12 + rows) shade = Color.Lerp(shade, new Color(.66f, .72f, .74f, 1), .32f);
                    if (x == 35) shade = Color.Lerp(shade, original.GetPixel(x, y), .35f);
                    texture.SetPixel(x, y, shade);
                }
            // Native inventory Images read sprite pixels for alpha hit tests.
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 48, 48), new Vector2(.5f, .5f), 100);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            entry = (texture, sprite); WaterLevels.Add((level, quality), entry);
        }
        if (!NativeAccess.Atlases.ContainsKey(WaterAtlasKey))
            NativeAccess.Atlases.Add(WaterAtlasKey, new System.Collections.Generic.Dictionary<string, Sprite>());
        var variants = NativeAccess.Atlases[WaterAtlasKey];
        string key = $"level_{level}_quality_{quality}";
        if (!variants.ContainsKey(key)) variants.Add(key, entry.Sprite);
        // SetSprite also marks the native item for redraw. Saved paths can
        // already match while the UI still shows an earlier missing sprite.
        item.SetSprite(WaterAtlasKey, key);
    }
    internal static void Clear()
    {
        NativeAccess.Atlases.Remove(WaterAtlasKey);
        foreach (var entry in WaterLevels.Values)
        { UnityEngine.Object.Destroy(entry.Sprite); UnityEngine.Object.Destroy(entry.Texture); }
        WaterLevels.Clear();
        if (_waterTemplate != null) UnityEngine.Object.Destroy(_waterTemplate);
        _waterTemplate = null; Atlas.Clear(); _log = null;
    }
}
