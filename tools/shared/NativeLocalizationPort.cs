#nullable enable
using System.Collections.Generic;

// Native Unity string tables and ModLoader are unavailable in the standalone checks.
public static class LocHelper
{
    public static string LocaleCode = "en";
    public static bool LocaleSelectionReady = true;
    public static int LocaleReads;
    public static string GetCurrentLocaleCode()
    {
        LocaleReads++;
        return LocaleSelectionReady ? LocaleCode : throw new System.InvalidOperationException("A synchronous locale read would block.");
    }
    public static string GetLocalizedItem(string key) => key;
    public static string GetLocalizedMechanic(string key) => key;
    public static string GetLocalizedOrDefault(string table, string key, string fallback) => fallback;
}

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded }
}

namespace UnityEngine.Localization.Settings
{
    using UnityEngine.ResourceManagement.AsyncOperations;

    public static partial class LocalizationSettings
    {
        public static bool HasSettings => true;
        public static LocaleOperation InitializationOperation => new();
        public static LocaleOperation SelectedLocaleAsync
        {
            get { LocHelper.LocaleReads++; return new(); }
        }
    }

    public sealed class LocaleOperation
    {
        public bool IsValid() => true;
        public bool IsDone => LocHelper.LocaleSelectionReady;
        public AsyncOperationStatus Status => IsDone ? AsyncOperationStatus.Succeeded : AsyncOperationStatus.None;
        public TestLocale Result => IsDone ? new() : throw new System.InvalidOperationException("Locale selection is still pending.");
    }

    public sealed class TestLocale
    {
        public TestLocale Identifier => this;
        public string Code => LocHelper.LocaleCode;
    }
}

public static class ModLoader
{
    public static readonly List<LoadedMod> Mods = new();
}

public sealed class LoadedMod
{
    public sealed class TestManifest { public string ID = ""; }
    public TestManifest Manifest = new();
    public bool IsEnabled;
    public object? Instance;
    public Dictionary<string, object> Sprites = new();
    public Dictionary<string, Dictionary<string, string>> Localization = new(System.StringComparer.OrdinalIgnoreCase);
}
