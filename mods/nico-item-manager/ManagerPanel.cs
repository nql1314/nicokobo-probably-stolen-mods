using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NicoItemManager
{
    internal sealed partial class ManagerPanel
    {
        enum View { Catalog, Details, Presets }
        const int PageSize = 18;
        readonly NicoItemManagerMod owner;
        readonly List<CatalogEntry> filtered = new List<CatalogEntry>();
        CustomUIWindow window;
        CustomUIElement statusLabel, progressLabel;
        RectTransform body, catalogRoot, editorRoot, catalogToolsRoot, navigationRoot, presetsRoot;
        GameObject backdrop;
        View view;
        SpawnDestination spawnDestination = SpawnDestination.DesktopFirst;
        string query = "", collection, category, status = "";
        CatalogEntry selected;
        ItemEditor editor;
        int page, blockedFrame = -1, catalogRevision = -1;
        ItemCatalog observedCatalog;
        bool dirty, editorLayoutDirty;
        float searchAt;

        internal ManagerPanel(NicoItemManagerMod owner) { this.owner = owner; }
        internal bool IsVisible => window != null && window.IsVisible;
        internal bool BlocksGameInput => IsVisible || blockedFrame == Time.frameCount;

        internal static bool IsTextEditing()
        {
            if (!Application.isFocused || !string.IsNullOrEmpty(Input.compositionString)
                || StoreUIManager.IsTextFieldFocused()) return true;
            GameObject focus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            TMP_InputField input = focus != null ? focus.GetComponentInParent<TMP_InputField>() : null;
            InputField legacy = focus != null ? focus.GetComponentInParent<InputField>() : null;
            return input != null && input.isActiveAndEnabled && input.isFocused
                || legacy != null && legacy.isActiveAndEnabled && legacy.isFocused;
        }

        internal void Toggle()
        {
            if (IsVisible) { Close(); return; }
            if (CustomUIManager.Instance == null) return;
            if (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem) return;
            view = View.Catalog;
            CustomUIBuilder builder = CustomUIManager.Instance.CreateWindow(NicoItemManagerMod.Id + ".window",
                    Text.Get("title"), CustomUIManager.LAYER_OVERLAY, style: CustomUIManager.Style.Machinery)
                .SetSize(1840, 1000).SetDraggable(true).SetCloseOnEscape(false).OnClose(Closed);
            window = builder.Handle;
            CreateBackdrop();
            Canvas.ForceUpdateCanvases();
            if (window.Rect.parent is RectTransform canvas && canvas.rect.width > 0 && canvas.rect.height > 0)
                window.SetSize(Mathf.Min(1840, canvas.rect.width - 48), Mathf.Min(1000, canvas.rect.height - 48));
            ApplyScale();
            Canvas.ForceUpdateCanvases();
            builder.BeginColumn(6);
            navigationRoot = builder.CurrentContainer;
            builder.BeginRow(12);
            builder.AddButton(Text.Get("catalog"), BackToCatalog);
            builder.Last.SetFixedWidth(180);
            builder.AddButton(Text.Get("custom_items"), OpenPresets);
            builder.Last.SetFixedWidth(200);
            builder.AddFlexibleSpace();
            builder.AddLabel(Text.Get("spawn_destination"));
            Label(builder.Last.SetFixedWidth(90), 18);
            builder.AddDropdown(new List<string> { Text.Get("desktop_first"), Text.Get("backpack") },
                (int)spawnDestination, index => spawnDestination = (SpawnDestination)index, id: "spawn-destination");
            builder.Last.SetFixedWidth(190);
            builder.End();
            builder.End();
            builder.BeginColumn(6);
            catalogToolsRoot = builder.CurrentContainer;
            AddTextInput(builder, Text.Get("search"), query,
                id: "search", onTyping: value => { query = value; searchAt = Time.unscaledTime + 0.12f; });
            builder.Last.SetPreferredSize(0, 40);
            StyleControls(window.ContentRoot);
            BuildFilters(builder);
            builder.AddLabel(Text.Get("click_an_item_to_view_or_edit"));
            Label(builder.Last, 16, true);
            builder.End();
            builder.BeginColumn(6);
            body = builder.CurrentContainer;
            Flexible(body);
            builder.BeginColumn(6);
            catalogRoot = builder.CurrentContainer;
            Flexible(catalogRoot);
            builder.End();
            builder.BeginColumn(6);
            editorRoot = builder.CurrentContainer;
            Flexible(editorRoot);
            editorRoot.gameObject.SetActive(false);
            builder.End();
            builder.BeginColumn(6);
            presetsRoot = builder.CurrentContainer;
            Flexible(presetsRoot);
            presetsRoot.gameObject.SetActive(false);
            builder.End();
            builder.End();
            builder.AddLabel("", "progress");
            progressLabel = builder.Last;
            Label(progressLabel, 16);
            builder.AddLabel(status, "status");
            statusLabel = builder.Last;
            Label(statusLabel, 16, true);
            statusLabel.SetPreferredSize(0, 42).SetVisible(!string.IsNullOrWhiteSpace(status));
            UpdateFilters();
            Filter();
            Rebuild();
            builder.Center().Show();
        }

        internal void ApplyScale()
        {
            if (window == null || !window.IsAlive) return;
            RectTransform rect = window.Rect;
            float scale = 0.75f * owner.UiScale.Value;
            if (rect.parent is RectTransform canvas && rect.rect.width > 0 && rect.rect.height > 0)
                scale = Mathf.Min(scale, (canvas.rect.width - 48) / rect.rect.width,
                    (canvas.rect.height - 48) / rect.rect.height);
            rect.localScale = Vector3.one * scale;
        }

        static void Flexible(RectTransform rect)
        {
            LayoutElement layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.flexibleHeight = layout.flexibleWidth = 1;
            layout.minHeight = 80;
        }

        void CreateBackdrop()
        {
            backdrop = new GameObject("NicoItemManager.ModalBackdrop", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)backdrop.transform;
            rect.SetParent(window.root.transform.parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.SetSiblingIndex(window.root.transform.GetSiblingIndex());
            var image = backdrop.GetComponent<Image>();
            image.color = new Color(0, 0, 0, 0.3f);
            image.raycastTarget = true;
        }

        internal void Close()
        {
            if (IsVisible) blockedFrame = Time.frameCount;
            if (window != null) window.Close();
        }

        void Closed()
        {
            blockedFrame = Time.frameCount;
            GameObject focus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (focus != null && window?.root != null && focus.transform.IsChildOf(window.root.transform))
                EventSystem.current.SetSelectedGameObject(null);
            if (backdrop != null)
            {
                backdrop.SetActive(false);
                UnityEngine.Object.Destroy(backdrop);
                backdrop = null;
            }
            RememberDraft();
            editor?.Dispose();
            editor = null;
            pendingDelete = null;
            previewGraphic = null;
            window = null;
            body = catalogRoot = editorRoot = catalogToolsRoot = navigationRoot = presetsRoot = null;
            presetLayoutDirty = true;
            editingPresetId = pendingPresetDelete = null;
            statusLabel = progressLabel = null;
            categoryFilterRoot = null;
            collectionChips.Clear();
            categoryChips.Clear();
            filtersLayoutDirty = false;
            rows.Clear();
            catalogBuilt = false;
            catalogList = null;
            columnWidths = null;
            emptyCatalog = previousPage = nextPage = pageLabel = exportButton = null;
            moduleInputs.Clear();
            propertyValues.Clear();
            gunConditionInput = null;
            editorSpawnButtons.Clear();
            previewName = previewInfo = previewDescription = shapeInput = effect1Input = effect2Input = null;
            capacityInput = liquidAmountInput = null;
            effectIds = null;
            selected = null;
            searchAt = 0;
            dirty = editorLayoutDirty = false;
        }

        internal void SceneChanged()
        {
            Close();
            filtered.Clear();
            collection = category = null;
            page = 0;
            status = "";
        }

        internal void Tick()
        {
            if (!IsVisible) return;
            if (Input.GetKeyDown(KeyCode.Escape) && !KeyBindHelper.SuppressHotkeys
                && string.IsNullOrEmpty(Input.compositionString))
            {
                blockedFrame = Time.frameCount;
                if (view == View.Catalog) { Close(); return; }
                if (editor?.IsExisting == true) { Close(); return; }
                if (view == View.Details && editorReturnView == View.Presets) OpenPresets();
                else BackToCatalog();
            }
            if (searchAt > 0 && Time.unscaledTime >= searchAt)
            {
                searchAt = 0;
                page = 0;
                BackToCatalog();
                Filter();
                RequestRebuild();
            }
            if (observedCatalog != owner.Catalog || catalogRevision != owner.Catalog.Revision) CatalogChanged();
            if (dirty) Rebuild();
            progressLabel?.SetText(owner.IsScanning
                ? Text.Get("reading_items", owner.Catalog.Scanned, owner.Catalog.Total)
                : Text.Get("items", filtered.Count, owner.Catalog.Entries.Count));
        }

        internal void RequestRebuild() => dirty = true;
        internal void SetStatus(string message)
        {
            status = message ?? "";
            statusLabel?.SetText(status).SetVisible(!string.IsNullOrWhiteSpace(status));
        }

        internal void CatalogChanged()
        {
            observedCatalog = owner.Catalog;
            catalogRevision = owner.Catalog.Revision;
            UpdateFilters();
            Filter();
            RequestRebuild();
        }

        void Filter() => owner.Catalog.FilterInto(filtered, query, category, collection);

        void Rebuild()
        {
            dirty = false;
            if (body == null || window == null || !window.IsAlive) return;
            bool layoutChanged = catalogRoot.gameObject.activeSelf != (view == View.Catalog);
            bool showNavigation = editor?.IsExisting != true;
            layoutChanged |= navigationRoot.gameObject.activeSelf != showNavigation;
            navigationRoot.gameObject.SetActive(showNavigation);
            bool showCatalogTools = showNavigation && view == View.Catalog;
            layoutChanged |= catalogToolsRoot.gameObject.activeSelf != showCatalogTools;
            catalogToolsRoot.gameObject.SetActive(showCatalogTools);
            progressLabel?.SetVisible(showCatalogTools);
            layoutChanged |= filtersLayoutDirty;
            filtersLayoutDirty = false;
            catalogRoot.gameObject.SetActive(view == View.Catalog);
            editorRoot.gameObject.SetActive(view == View.Details);
            layoutChanged |= presetsRoot.gameObject.activeSelf != (view == View.Presets);
            presetsRoot.gameObject.SetActive(view == View.Presets);
            if (view == View.Catalog)
            {
                layoutChanged |= !catalogBuilt;
                if (!catalogBuilt) BuildCatalog();
                layoutChanged |= RefreshCatalog();
            }
            else if (view == View.Details)
            {
                layoutChanged |= editorLayoutDirty || previewNeedsRefresh;
                if (editorLayoutDirty) BuildEditor();
                RefreshEditor();
            }
            else if (presetLayoutDirty)
            {
                BuildPresets();
                layoutChanged = true;
            }
            if (layoutChanged) CustomUIBuilder.RebuildLayout(window.ContentRoot);
        }

        void BackToCatalog()
        {
            GameObject focus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (focus != null && editorRoot != null && focus.transform.IsChildOf(editorRoot))
                EventSystem.current.SetSelectedGameObject(null);
            if (editorRoot != null) editorRoot.gameObject.SetActive(false);
            RememberDraft();
            editor?.Dispose();
            editor = null;
            selected = null;
            view = View.Catalog;
            RequestRebuild();
        }

        void Spawn(string id, int count, Action<GameItem> configure = null)
        {
            if (configure == null && drafts.TryGetValue(id, out var draft)) configure = draft.ApplyTo;
            SetStatus(ItemSpawner.Spawn(id, count, configure: configure, destination: spawnDestination).Message);
        }

        void Export()
        {
            try { SetStatus(Text.Get("exported_to", CatalogExporter.Export(owner.Catalog))); }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning("Export failed: " + error);
                SetStatus(Text.Get("export_failed", error.Message));
            }
        }

        static void Fixed(CustomUIElement element, float width, float height) =>
            element?.SetFixedWidth(width).SetPreferredSize(width, height);

        static void Label(CustomUIElement element, float size, bool wrap = false)
        {
            if (element?.text == null) return;
            element.text.richText = false;
            element.text.fontSize = size;
            element.text.enableAutoSizing = false;
            element.text.enableWordWrapping = wrap;
            element.text.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            element.text.alignment = TextAlignmentOptions.MidlineLeft;
        }
    }

    public sealed class CatalogRow : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        internal Action OnSelect;
        internal Image Background;
        internal Color NormalColor;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.used) return;
            if (eventData.pointerPress != null && eventData.pointerPress != gameObject
                && eventData.pointerPress.GetComponentInParent<Button>() != null) return;
            if (eventData.button == PointerEventData.InputButton.Left || eventData.button == PointerEventData.InputButton.Right)
                OnSelect?.Invoke();
        }
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Background != null) Background.color = new Color(0.84f, 0.82f, 0.72f, 0.18f);
        }
        public void OnPointerExit(PointerEventData eventData)
        {
            if (Background != null) Background.color = NormalColor;
        }
    }
}
