using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NicoItemManager
{
    internal sealed partial class ManagerPanel
    {
        readonly Dictionary<string, ItemConfiguration> drafts = new Dictionary<string, ItemConfiguration>(StringComparer.Ordinal);
        readonly ItemPresets presets = new ItemPresets();
        View editorReturnView;
        string presetName = "", editingPresetId, pendingPresetDelete;
        int presetPage;
        bool presetLayoutDirty = true;

        void RememberDraft()
        {
            if (editor != null && !editor.IsExisting && selected != null) drafts[selected.Id] = editor.Capture();
        }

        void OpenPresets()
        {
            try { presets.Load(); }
            catch (Exception error) { SetStatus(error.Message); return; }
            BackToCatalog();
            view = View.Presets;
            searchAt = 0;
            pendingPresetDelete = null;
            presetLayoutDirty = true;
            RequestRebuild();
        }

        void BuildPresetActions(CustomUIBuilder builder)
        {
            builder.BeginRow(8);
            AddTextInput(builder, Text.Get("custom_item_name"), presetName,
                onTyping: value => presetName = value);
            builder.Last.SetFlexible(1, 0).SetPreferredSize(0, 40);
            builder.AddButton(Text.Get("save_as_new"), () => SavePreset(false));
            builder.Last.SetFixedWidth(200);
            if (editingPresetId != null)
            {
                builder.AddButton(Text.Get("update_preset"), () => SavePreset(true));
                builder.Last.SetFixedWidth(200);
            }
            builder.End();
        }

        void SavePreset(bool replace)
        {
            try
            {
                ItemPreset saved = presets.Save(presetName, selected.Id, editor.Capture(), replace ? editingPresetId : null);
                editingPresetId = saved.Id;
                presetName = saved.Name;
                editorLayoutDirty = presetLayoutDirty = true;
                RequestRebuild();
                SetStatus(Text.Get("saved_custom_item", saved.Name));
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning("Save custom item: " + error);
                SetStatus(error.Message);
            }
        }

        void BuildPresets()
        {
            presetLayoutDirty = false;
            CustomUIBuilder.ClearContainer(presetsRoot);
            var builder = new CustomUIBuilder(CustomUIManager.Instance, window, presetsRoot);
            builder.AddLabel(Text.Get("edit_and_save_a_preset_in_item"));
            Label(builder.Last, 16, true);
            if (pendingPresetDelete != null)
            {
                ItemPreset pending = presets.Entries.First(item => item.Id == pendingPresetDelete);
                builder.BeginRow(8);
                builder.AddLabel(Text.Get("delete_preset", pending.Name));
                Label(builder.Last.SetFlexible(1, 0), 18);
                builder.AddButton(Text.Get("confirm_delete"), () =>
                {
                    try
                    {
                        presets.Delete(pending.Id);
                        pendingPresetDelete = null;
                        presetLayoutDirty = true;
                        RequestRebuild();
                        SetStatus(Text.Get("preset_deleted"));
                    }
                    catch (Exception error) { SetStatus(error.Message); }
                });
                builder.Last.SetFixedWidth(180);
                builder.AddButton(LocHelper.GetLocalizedMainMenu("load_cancel"), () =>
                { pendingPresetDelete = null; presetLayoutDirty = true; RequestRebuild(); });
                builder.Last.SetFixedWidth(120);
                builder.End();
            }
            builder.BeginScrollFlexible(80);
            int pages = Math.Max(1, (presets.Entries.Count + PageSize - 1) / PageSize);
            presetPage = Math.Max(0, Math.Min(presetPage, pages - 1));
            if (presets.Entries.Count == 0)
            {
                builder.AddLabel(Text.Get("no_custom_items_yet_choose_an_item"));
                Label(builder.Last, 18, true);
            }
            foreach (ItemPreset preset in presets.Entries.Skip(presetPage * PageSize).Take(PageSize))
            {
                bool available = ItemSpawner.IsStoreReady() && DirectoryMaster.Has<GameItem>(preset.ItemId);
                builder.BeginRow(8, TextAnchor.MiddleLeft);
                builder.AddLabel(preset.Name + " · " + preset.ItemId + (available ? "" : Text.Get("item_unavailable")));
                Label(builder.Last.SetFlexible(1, 0), 18);
                foreach (int count in new[] { 1, 5 })
                {
                    int copies = count;
                    builder.AddButton(Text.Get("spawn_2", copies),
                        () => Spawn(preset.ItemId, copies, preset.Configuration.ApplyTo));
                    builder.Last.SetFixedWidth(150).SetInteractable(available);
                }
                builder.AddButton(Text.Get("edit"), () => OpenEditor(new CatalogEntry { Id = preset.ItemId }, preset: preset));
                builder.Last.SetFixedWidth(110).SetInteractable(available);
                builder.AddButton(Text.Get("delete"), () =>
                { pendingPresetDelete = preset.Id; presetLayoutDirty = true; RequestRebuild(); });
                builder.Last.SetFixedWidth(110);
                builder.End();
            }
            builder.End();
            builder.BeginRow(8);
            builder.AddButton(Text.Get("previous"), () =>
            { presetPage--; presetLayoutDirty = true; RequestRebuild(); });
            builder.Last.SetFixedWidth(124).SetInteractable(presetPage > 0);
            builder.AddLabel((presetPage + 1) + " / " + pages);
            Label(builder.Last.SetFixedWidth(100), 18);
            builder.AddButton(Text.Get("next"), () =>
            { presetPage++; presetLayoutDirty = true; RequestRebuild(); });
            builder.Last.SetFixedWidth(124).SetInteractable(presetPage + 1 < pages);
            builder.End();
            StyleControls(presetsRoot);
        }
    }
}
