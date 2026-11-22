using System;
using System.Collections.Generic;
using System.Diagnostics;
using Nicokobo.ModUtilities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace NicoItemManager
{
    public sealed class NicoItemManagerMod : IMod
    {
        internal const string Id = "nicokobo.item-manager";
        const string ConfigSection = "manager.config.nico_item_manager";
        internal static NicoItemManagerMod Active { get; private set; }
        internal static ModLog Log { get; private set; }
        internal ConfigEntry<float> UiScale { get; private set; }
        ConfigEntry<string> hotkey;
        ConfigEntry<string> manageItemHotkey;
        readonly PanelHotkey binding = new PanelHotkey();
        readonly PanelHotkey manageItemBinding = new PanelHotkey();
        internal ItemCatalog Catalog { get; private set; } = new ItemCatalog();
        internal ManagerPanel Panel { get; private set; }
        IEnumerator<object> scan;
        long scanStarted;
        int scanFrames;
        GameObject host;
        string locale;
        bool scanFailed, scanIndexPublished;
        internal bool IsScanning => scan != null;

        public void Init(ModManifest manifest)
        {
            Log = new ModLog(manifest);
            ModConfig config = ModHelper.GetConfig(manifest.ID);
            hotkey = config.Define("Hotkey", "Home", ConfigSection,
                () => Text.Get("hotkey"),
                tooltip: () => Text.Get("hotkey_help"));
            manageItemHotkey = config.Define("ManageItemHotkey", "End", ConfigSection,
                () => Text.Get("manage_item_hotkey"),
                tooltip: () => Text.Get("manage_item_hotkey_help") + " " + Text.Get("hotkey_help"));
            UiScale = config.Define("UiScale", 1f, ConfigSection,
                () => Text.Get("scale"), 0.5f, 2f,
                tooltip: () => Text.Get("scale_the_whole_window_proportionally_default_1"));
        }

        public void OnEnable()
        {
            Active = this;
            Panel = new ManagerPanel(this);
            locale = null;
            try
            {
                ModHook.OnGameLoadedLate += ResetCatalog;
                ModHook.OnModConfigApplied += ConfigApplied;
                SceneManager.activeSceneChanged += SceneChanged;
                host = new GameObject("NicoKobo.Nico' Item Manager");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<NicoItemManagerRuntime>().Owner = this;
                Log.Log("1.3.5 ready; hotkey=" + hotkey.Value);
            }
            catch { OnDisable(); throw; }
        }

        public void OnDisable()
        {
            ModHook.OnGameLoadedLate -= ResetCatalog;
            ModHook.OnModConfigApplied -= ConfigApplied;
            SceneManager.activeSceneChanged -= SceneChanged;
            scan?.Dispose();
            scan = null;
            ExistingItemMenu.Clear();
            Panel?.Close();
            Panel = null;
            if (host != null)
            {
                host.SetActive(false);
                UnityEngine.Object.Destroy(host);
                host = null;
            }
            if (ReferenceEquals(Active, this)) Active = null;
        }

        void SceneChanged(Scene previous, Scene next)
        {
            ExistingItemMenu.Clear();
            ResetCatalog();
        }
        void ConfigApplied(string id)
        {
            if (id != Id) return;
            Panel.ApplyScale();
            Panel.RequestRebuild();
        }

        void ResetCatalog()
        {
            scan?.Dispose();
            scan = null;
            scanFailed = false;
            Catalog = new ItemCatalog();
            Panel.SceneChanged();
        }

        internal void Rescan()
        {
            if (scan != null || !ItemSpawner.IsStoreReady()) return;
            scanFailed = false;
            scan = Catalog.Scan();
            scanIndexPublished = false;
            scanStarted = Stopwatch.GetTimestamp();
            scanFrames = 0;
            Panel.RequestRebuild();
        }

        internal void Tick()
        {
            bool storeReady = ItemSpawner.IsStoreReady();
            if (storeReady)
            {
                if (!ModText.NativeTablesReady) return;
                string selectedLocale = Text.LocaleCode;
                if (!string.Equals(locale, selectedLocale, StringComparison.Ordinal))
                {
                    locale = selectedLocale;
                    ResetCatalog();
                }
            }
            if (!storeReady)
            {
                scan?.Dispose();
                scan = null;
            }
            else if (!scanFailed)
            {
                if (scan == null && !Catalog.Ready) Rescan();
                AdvanceScan();
            }
            if (!KeyBindHelper.SuppressHotkeys && !ManagerPanel.IsTextEditing())
            {
                if (binding.WasPressed(hotkey.Value)) Panel.Toggle();
                else if (storeReady && !Panel.BlocksGameInput && manageItemBinding.WasPressed(manageItemHotkey.Value))
                    OpenHoveredItem();
            }
            Panel.Tick();
            if (Panel.BlocksGameInput) CustomEventHandler.ClearDoubleClickTarget();
        }

        void OpenHoveredItem()
        {
            if (EventSystem.current == null || StoreUIManager.IsAnyTyping() || UIDragHandler.isDraggingAnyUI
                || ItemMouseDragHandler.current?.IsDraggingItem == true || ItemSelectHandler.current?.IsEquipped == true
                || ItemPinHelper.BlocksClicks) return;

            GameItem item = RenderHandler.RaycastElement<GameItem>(Input.mousePosition);
            if (item != null && !GeneralHelper.IsDisplayOnly(item) && ExistingItems.UnavailableReason(item) == null)
                Panel.OpenExistingItem(item);
        }

        void AdvanceScan()
        {
            if (scan == null) return;
            try
            {
                scanFrames++;
                // Spend more of the frame on the catalog while the user is waiting
                // in its panel; keep background work at the existing 2 ms budget.
                bool publishingIndex = !scanIndexPublished;
                scanIndexPublished = true;
                // Render the identifier page before localization/factory work starts.
                bool finished = publishingIndex ? !scan.MoveNext()
                    : ItemCatalog.AdvanceScan(scan, Panel.IsVisible ? 6 : 2);
                if (publishingIndex) Panel.CatalogChanged();
                if (finished)
                {
                    double elapsedMs = (Stopwatch.GetTimestamp() - scanStarted) * 1000.0 / Stopwatch.Frequency;
                    scan.Dispose();
                    scan = null;
                    Panel.CatalogChanged();
                    Log.Log($"Catalog: {Catalog.Entries.Count} registered items in {elapsedMs:F0} ms ({scanFrames} frames).");
                }
            }
            catch (Exception error)
            {
                scan?.Dispose();
                scan = null;
                scanFailed = true;
                Log.Error("Catalog scan failed: " + error);
                Panel.SetStatus(Text.Get("item_scan_failed_see_the_f8_log"));
            }
        }
    }

    [DefaultExecutionOrder(-100)]
    public sealed class NicoItemManagerRuntime : MonoBehaviour
    {
        internal NicoItemManagerMod Owner;
        void Update() => Owner?.Tick();

        void LateUpdate()
        {
            // Clear again after native Update so the final panel click cannot
            // become the first half of a double-click after the panel closes.
            if (Owner?.Panel?.BlocksGameInput == true) CustomEventHandler.ClearDoubleClickTarget();
        }
    }
}
