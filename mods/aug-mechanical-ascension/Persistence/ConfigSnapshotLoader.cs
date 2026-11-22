using System.Reflection;
using System.Text;
using AugMechanicalAscension.Serialization;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Persistence;

internal enum ConfigLoadStatus { Defaults, Loaded, Invalid, Unavailable }
internal sealed record ConfigLoadResult(ConfigLoadStatus Status, ConfigSnapshot Config, string? Error);

// Reads new-run defaults only. Existing runs always use their saved ConfigSnapshot.
internal static class ConfigSnapshotLoader
{
    internal const int MaxBytes = AugNumbers.ConfigSnapshotLoaderLimits.MaxBytes;
    internal const int MaxSettings = AugNumbers.ConfigSnapshotLoaderLimits.MaxSettings;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = AugNumbers.Validation.ConfigJsonDepth
    };

    internal static ConfigLoadResult ParseBytes(byte[]? bytes)
    {
        if (bytes == null) return new(ConfigLoadStatus.Defaults, new ConfigSnapshot(), null);
        if (bytes.Length > MaxBytes) return Invalid($"Configuration exceeds {MaxBytes} bytes");
        try
        {
            return Parse(StrictUtf8.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            return Invalid("Configuration is not valid UTF-8");
        }
    }

    // Only our four configuration records are inspected. Callers cannot provide
    // a runtime type that widens the reflection scope beyond this assembly.
    private static readonly IReadOnlyDictionary<Type, PropertyInfo[]> Schemas = new Dictionary<Type, PropertyInfo[]>
    {
        [typeof(ConfigSnapshot)] = typeof(ConfigSnapshot).GetProperties(BindingFlags.Instance | BindingFlags.Public),
        [typeof(CyberneticConfig)] = typeof(CyberneticConfig).GetProperties(BindingFlags.Instance | BindingFlags.Public),
        [typeof(CyberneticMaterialConfig)] = typeof(CyberneticMaterialConfig).GetProperties(BindingFlags.Instance | BindingFlags.Public),
        [typeof(WorkshopMaterialCost)] = typeof(WorkshopMaterialCost).GetProperties(BindingFlags.Instance | BindingFlags.Public)
    };

    internal static ConfigLoadResult Parse(string? json)
    {
        if (json == null) return Invalid("Configuration is null");
        try
        {
            if (StrictUtf8.GetByteCount(json) > MaxBytes)
                return Invalid($"Configuration exceeds {MaxBytes} bytes");
            if (json.Length > 0 && json[0] == '\uFEFF') json = json[1..];
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = AugNumbers.Validation.ConfigJsonDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Invalid("Configuration root must be an object");
            int settingCount = 0;
            ValidateNames(document.RootElement, typeof(ConfigSnapshot), ref settingCount);
            var config = JsonSerializer.Deserialize<ConfigSnapshot>(json, JsonOptions)
                ?? throw new JsonException("Configuration is null");
            // Native opening Specs grant exactly one of each. Keep saved
            // snapshot validation historical; reject unsupported new input here.
            if (config.InitialBatteryCount != AugNumbers.OpeningAndSurvival.InitialBatteryCount)
                return Invalid("initialBatteryCount is fixed at 1 for the native opening");
            if (config.InitialMedicalCount != AugNumbers.OpeningAndSurvival.InitialMedicalCount)
                return Invalid("initialMedicalCount is fixed at 1 for the native opening");
            if (config.InitialElectronicCount != AugNumbers.OpeningAndSurvival.InitialElectronicCount)
                return Invalid("initialElectronicCount is fixed at 1 for the native opening");
            if (config.NeuralModuleTestPrice != 100)
                return Invalid("Brain-Computer Interface base price is fixed at 100");
            // Adapter evidence IDs are not user-provided capabilities. Keep this empty
            // until each native model adapter is shipped and verified with the build.
            if (config.MachineProfiles is { Length: > 0 })
                return Invalid("Machine profiles require verified native adapters");
            RunStateCodec.ValidateConfig(config);
            return new(ConfigLoadStatus.Loaded, config, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or
                                   ArgumentException)
        {
            return Invalid($"Invalid configuration: {ex.Message}");
        }
    }

    private static void ValidateNames(JsonElement element, Type recordType, ref int settingCount)
    {
        var names = Schemas[recordType]
            .ToDictionary(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                property => property.PropertyType, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in element.EnumerateObject())
        {
            if (++settingCount > MaxSettings)
                throw new JsonException($"Configuration exceeds {MaxSettings} settings");
            if (!seen.Add(entry.Name)) throw new JsonException($"Duplicate setting: {entry.Name}");
            if (!names.TryGetValue(entry.Name, out var propertyType))
                throw new JsonException($"Unknown setting: {entry.Name}");
            if ((propertyType == typeof(CyberneticConfig) ||
                 propertyType == typeof(CyberneticMaterialConfig)) &&
                entry.Value.ValueKind == JsonValueKind.Object)
                ValidateNames(entry.Value, propertyType, ref settingCount);
            if (propertyType == typeof(Dictionary<string, WorkshopMaterialCost[]>))
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                    throw new JsonException("unlockRecipes must be an object");
                var recipeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var recipe in entry.Value.EnumerateObject())
                {
                    if (++settingCount > MaxSettings ||
                        !recipeNames.Add(recipe.Name) ||
                        !CyberneticRules.IsRecipeKey(recipe.Name) ||
                        recipe.Value.ValueKind != JsonValueKind.Array)
                        throw new JsonException($"Invalid unlock recipe: {recipe.Name}");
                    foreach (var ingredient in recipe.Value.EnumerateArray())
                    {
                        if (++settingCount > MaxSettings ||
                            ingredient.ValueKind != JsonValueKind.Object)
                            throw new JsonException($"Invalid ingredient: {recipe.Name}");
                        ValidateNames(ingredient, typeof(WorkshopMaterialCost),
                            ref settingCount);
                    }
                }
            }
        }
    }

    private static ConfigLoadResult Invalid(string message)
        => new(ConfigLoadStatus.Invalid, new ConfigSnapshot(), message);
}
