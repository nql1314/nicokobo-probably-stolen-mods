using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace NicoItemManager
{
    internal sealed partial class ManagerPanel
    {
        readonly Dictionary<string, CustomUIElement> moduleInputs = new Dictionary<string, CustomUIElement>();
        readonly List<CustomUIElement> editorSpawnButtons = new List<CustomUIElement>();
        CustomUIElement previewName, previewInfo, previewDescription, shapeInput, effect1Input, effect2Input,
            capacityInput, liquidAmountInput;
        ItemPreviewGraphic previewGraphic;
        List<string> effectIds;
        bool previewNeedsRefresh, showAdvanced;
        int liquidPreset;
        string newKey = "", newValue = "";
        bool newBool;
        TagValueKind newKind;

        void OpenEditor(CatalogEntry entry, bool reset = false, ItemPreset preset = null)
        {
            if (entry == null) return;
            try
            {
                if (!ItemSpawner.IsStoreReady()) throw new InvalidOperationException(Text.Get("enter_the_store_first"));
                RememberDraft();
                var next = new ItemEditor(entry.Id);
                try
                {
                    if (preset != null) next.Restore(preset.Configuration);
                    else if (!reset && drafts.TryGetValue(entry.Id, out var draft)) next.Restore(draft);
                }
                catch { next.Dispose(); throw; }
                if (reset) drafts.Remove(entry.Id);
                editorReturnView = preset != null ? View.Presets : view == View.Details ? editorReturnView : View.Catalog;
                editor?.Dispose();
                editor = next;
                selected = entry;
                if (!reset)
                {
                    editingPresetId = preset?.Id;
                    presetName = preset?.Name ?? next.Item.GetDisplayName();
                }
                view = View.Details;
                editorLayoutDirty = previewNeedsRefresh = true;
                newKey = newValue = "";
                newBool = false;
                searchAt = 0;
                RequestRebuild();
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning("Read item editor: " + error);
                SetStatus(error.Message);
            }
        }

        void BuildEditor()
        {
            editorLayoutDirty = false;
            CustomUIBuilder.ClearContainer(editorRoot);
            moduleInputs.Clear();
            propertyValues.Clear();
            gunConditionInput = null;
            editorSpawnButtons.Clear();
            capacityInput = liquidAmountInput = shapeInput = effect1Input = effect2Input = null;
            var builder = new CustomUIBuilder(CustomUIManager.Instance, window, editorRoot);
            builder.BeginRow(8);
            builder.AddButton(editor.IsExisting ? Text.Get("cancel_and_close")
                    : editorReturnView == View.Presets ? Text.Get("back_to_presets") : Text.Get("back"),
                () => { if (editor.IsExisting) Close(); else if (editorReturnView == View.Presets) OpenPresets(); else BackToCatalog(); });
            builder.Last.SetFixedWidth(135);
            builder.AddButton(Text.Get("reset_edits"), () =>
            {
                if (editor.IsExisting) OpenExistingItem(editor.Target);
                else OpenEditor(selected, reset: true);
            });
            builder.Last.SetFixedWidth(155);
            builder.AddButton(Text.Get("copy_id"), () => GUIUtility.systemCopyBuffer = selected.Id);
            builder.Last.SetFixedWidth(130);
            builder.End();
            builder.AddLabel(editor.IsExisting
                ? Text.Get("apply_changes_to_update_the_existing_stack")
                : Text.Get("edits_affect_new_spawns_only_hover_the"));
            Label(builder.Last, 16, true);
            builder.BeginRow(16, TextAnchor.UpperLeft);
            builder.AddImage((Sprite)null, 160, 140);
            Fixed(builder.Last, 160, 140);
            builder.Last.image.color = Color.clear;
            builder.Last.image.raycastTarget = true;
            previewGraphic = new ItemPreviewGraphic((RectTransform)builder.Last.root.transform);
            builder.BeginColumn(4);
            builder.AddLabel("");
            previewName = builder.Last;
            Label(previewName, 21);
            builder.AddLabel("");
            previewInfo = builder.Last;
            Label(previewInfo, 16, true);
            builder.AddLabel("");
            previewDescription = builder.Last;
            Label(previewDescription, 15, true);
            previewDescription.SetPreferredSize(0, 76);
            builder.End();
            builder.End();
            builder.AddSeparator();
            builder.BeginScrollFlexible(80);
            if (editor.IsModule) BuildModuleFields(builder);
            else if (editor.IsExisting)
            {
                builder.BeginRow(10);
                NumberField(builder, Text.Get("unit_value"), value => editor.SetValue(ParseLong(value)), out var price);
                moduleInputs["value"] = price;
                builder.End();
                builder.AddSeparator();
            }
            if (editor.IsLiquid) BuildLiquidFields(builder);
            BuildItemPropertyFields(builder);
            if (editor.Item.identifier.IndexOf("ingot", StringComparison.OrdinalIgnoreCase) >= 0
                || editor.Item.state.dict.ContainsKey(IngotPurityHelper.INGOT_PURITY_TAG))
            {
                builder.BeginRow(6);
                for (int i = 0; i < ItemTags.PurityGrades.Length; i++)
                {
                    int index = i;
                    builder.AddButton(ItemTags.PurityLabel(i), () => SetPurity(index));
                    builder.Last.SetFixedWidth(110);
                }
                builder.AddButton(Text.Get("clear_purity"), () => SetPurity(-1));
                builder.Last.SetFixedWidth(150);
                builder.End();
            }
            BuildNumericTagFields(builder);
            builder.AddToggle(Text.Get("advanced_tags"), showAdvanced, value =>
            {
                showAdvanced = value;
                editorLayoutDirty = true;
                RequestRebuild();
            });
            if (showAdvanced) BuildAdvancedTags(builder);
            builder.End();
            if (editor.IsExisting) BuildExistingActions(builder);
            else
            {
                BuildPresetActions(builder);
                builder.BeginRow(8);
                foreach (int count in new[] { 1, 5 })
                {
                    int copies = count;
                    builder.AddButton(Text.Get("spawn_2", copies), () =>
                    {
                        ItemConfiguration configuration = editor.Capture();
                        drafts[selected.Id] = configuration;
                        Spawn(selected.Id, copies, configuration.ApplyTo);
                    });
                    editorSpawnButtons.Add(builder.Last.SetFixedWidth(150));
                }
                builder.End();
            }
            StyleControls(editorRoot);
            previewNeedsRefresh = true;
        }

        void BuildModuleFields(CustomUIBuilder builder)
        {
            builder.AddLabel(Text.Get("module"));
            Label(builder.Last, 19);
            builder.BeginRow(10);
            string[] titles = { Text.Get("performance"), Text.Get("efficiency"), Text.Get("quality") };
            for (int i = 0; i < ModuleEditing.Stats.Length; i++)
            {
                string key = ModuleEditing.Stats[i];
                NumberField(builder, titles[i], value => editor.SetModuleStat(key, ParseInt(value)), out var input);
                moduleInputs[key] = input;
            }
            builder.End();
            builder.BeginRow(10);
            NumberField(builder, Text.Get("tier_1_5"), value => editor.SetTier(ParseInt(value)), out var tier);
            moduleInputs["MODULE_TIER_INT"] = tier;
            NumberField(builder, Text.Get("unit_value"), value => editor.SetValue(ParseLong(value)), out var price);
            moduleInputs["value"] = price;
            builder.End();
            effectIds = ModuleEditing.EffectsFor(editor.Item);
            builder.BeginRow(10);
            for (int i = 1; i <= 2; i++)
            {
                int slot = i;
                builder.AddLabel(Text.Get("effect", slot));
                builder.Last.SetFixedWidth(80);
                builder.AddDropdown(effectIds.Select(ModuleEditing.EffectName).ToList(), effectIds.IndexOf(ModuleEditing.Effect(editor.Item, slot)),
                    index => ApplyEditor(() => ModuleEditing.SetEffect(editor.Item, slot, effectIds[index])));
                builder.Last.SetFixedWidth(300);
                if (slot == 1) effect1Input = builder.Last;
                else effect2Input = builder.Last;
            }
            builder.End();
            if (!editor.IsExisting && editor.Shapes.Count > 1)
            {
                builder.BeginRow(10);
                builder.AddLabel(Text.Get("shape"));
                builder.Last.SetFixedWidth(80);
                builder.AddDropdown(editor.Shapes.Select(shape => shape.Label).ToList(), 0,
                    index => ApplyEditor(() => editor.SetShape(index)));
                shapeInput = builder.Last.SetFixedWidth(650);
                builder.End();
            }
            builder.AddSeparator();
        }

        void BuildLiquidFields(CustomUIBuilder builder)
        {
            builder.AddLabel(Text.Get("liquid_container"));
            Label(builder.Last, 19);
            builder.BeginRow(10);
            NumberField(builder, Text.Get("capacity_ml"), value => editor.SetCapacity(ParseInt(value)), out capacityInput);
            NumberField(builder, Text.Get("amount_ml"), value => editor.SetLiquidAmount(ParseInt(value)), out liquidAmountInput);
            builder.AddButton(Text.Get("empty"), () => ApplyEditor(editor.EmptyLiquid));
            builder.Last.SetFixedWidth(120);
            builder.End();
            builder.BeginRow(10);
            builder.AddDropdown(LiquidEditing.Labels(), liquidPreset, index => liquidPreset = index);
            builder.Last.SetFixedWidth(320);
            builder.AddButton(Text.Get("fill_with_preset"), () => ApplyEditor(() => editor.FillLiquid(liquidPreset)));
            builder.Last.SetFixedWidth(200);
            builder.AddLabel(Text.Get("changing_the_amount_preserves_the_mixture"));
            Label(builder.Last, 16);
            builder.End();
            builder.AddSeparator();
        }

        void NumberField(CustomUIBuilder builder, string title, Action<string> apply, out CustomUIElement input)
        {
            builder.AddLabel(title);
            builder.Last.SetFixedWidth(130);
            AddTextInput(builder, "", "", value => ApplyEditor(() => apply(value)));
            input = builder.Last.SetFixedWidth(135);
        }

        void BuildAdvancedTags(CustomUIBuilder builder)
        {
            builder.AddLabel(Text.Get("edit_existing_values_above_manage_tag_types"));
            Label(builder.Last, 16, true);
            var numericKeys = new HashSet<string>(editor.ReadNumericTags().Select(edit => edit.Key), StringComparer.Ordinal);
            foreach (TagEdit edit in editor.ReadTags())
            {
                builder.BeginRow(6, TextAnchor.MiddleLeft);
                TagParameterLabel(builder, edit.Key);
                builder.AddDropdown(KindOptions(), (int)edit.Kind, index => ApplyEditor(() =>
                {
                    TagEdit current = editor.ReadTags().Single(tag => tag.Key == edit.Key);
                    current.Kind = (TagValueKind)index;
                    if ((current.Kind == TagValueKind.Float || current.Kind == TagValueKind.Double) && string.IsNullOrWhiteSpace(current.Text))
                        current.Text = "0";
                    editor.ApplyTag(current);
                }, true));
                builder.Last.SetFixedWidth(130);
                if (numericKeys.Contains(edit.Key))
                {
                    builder.AddLabel(edit.DisplayValue);
                    Label(builder.Last, 16);
                    builder.Last.SetTooltip(Text.Get("edit_this_in_item_values_above"));
                    propertyValues.Add(builder.Last, () => ItemTags.ValueText(editor.Item.GetTagReadonly(edit.Key), edit.Kind));
                }
                else if (edit.Kind == TagValueKind.Bool)
                    builder.AddToggle("true", edit.BoolValue, value => ApplyEditor(() =>
                    {
                        TagEdit current = editor.ReadTags().Single(tag => tag.Key == edit.Key);
                        current.BoolValue = value;
                        editor.ApplyTag(current);
                    }));
                else
                    AddTextInput(builder, Text.Get("value_2"), edit.Text, value => ApplyEditor(() =>
                    {
                        TagEdit current = editor.ReadTags().Single(tag => tag.Key == edit.Key);
                        current.Text = value;
                        editor.ApplyTag(current);
                    }));
                builder.Last.SetFixedWidth(260);
                builder.AddToggle(Text.Get("enabled"), edit.Enabled, value => { edit.Enabled = value; ApplyEditor(() => editor.ApplyTag(edit), true); });
                builder.Last.SetFixedWidth(125);
                builder.AddButton(Text.Get("remove"), () => { edit.Removed = true; ApplyEditor(() => editor.ApplyTag(edit), true); });
                builder.Last.SetFixedWidth(120);
                builder.End();
            }
            builder.BeginRow(6);
            AddTextInput(builder, Text.Get("new_tag_key"), newKey, onTyping: value => newKey = value);
            builder.Last.SetFixedWidth(300);
            builder.AddDropdown(KindOptions(), (int)newKind, index => { newKind = (TagValueKind)index; editorLayoutDirty = true; RequestRebuild(); });
            builder.Last.SetFixedWidth(130);
            if (newKind == TagValueKind.Bool) builder.AddToggle("true", newBool, value => newBool = value);
            else AddTextInput(builder, Text.Get("value_2"), newValue, onTyping: value => newValue = value);
            builder.Last.SetFixedWidth(260);
            builder.AddButton(Text.Get("add_tag"), () => ApplyEditor(() =>
            {
                string key = newKey.Trim();
                if (editor.Item.state.dict.ContainsKey(key)) throw new InvalidOperationException(Text.Get("this_tag_already_exists"));
                editor.ApplyTag(new TagEdit { Key = key, Kind = newKind, Text = newValue, BoolValue = newBool, IsNew = true });
                newKey = newValue = "";
                newBool = false;
            }, true));
            builder.Last.SetFixedWidth(150);
            builder.End();
        }

        static List<string> KindOptions() => new List<string> { ItemTags.KindLabel(TagValueKind.String),
            ItemTags.KindLabel(TagValueKind.Int), ItemTags.KindLabel(TagValueKind.Bool),
            ItemTags.KindLabel(TagValueKind.Float), ItemTags.KindLabel(TagValueKind.Double) };

        void SetPurity(int index) => ApplyEditor(() =>
        {
            TagEdit edit = ItemTags.ReadItem(editor.Item).Find(tag => tag.IsPurity)
                ?? new TagEdit { Key = IngotPurityHelper.INGOT_PURITY_TAG, IsNew = true };
            edit.Kind = TagValueKind.String;
            edit.Text = index < 0 ? "" : ItemTags.PurityGrades[index];
            edit.Removed = index < 0;
            edit.Enabled = index >= 0;
            editor.ApplyTag(edit);
        }, true);

        void ApplyEditor(Action change, bool rebuild = false)
        {
            try
            {
                change();
                RememberDraft();
                SetStatus("");
                editorLayoutDirty |= rebuild;
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning("Item edit rejected: " + error);
                SetStatus(error.Message);
                // Recreate fields from the accepted model, so invalid text cannot
                // remain onscreen while the preview and generated item use old data.
                editorLayoutDirty = true;
            }
            previewNeedsRefresh = true;
            RequestRebuild();
        }

        void RefreshEditor()
        {
            foreach (CustomUIElement button in editorSpawnButtons)
                button.SetInteractable(ItemSpawner.IsStoreReady());
            if (!previewNeedsRefresh || editor == null) return;
            previewNeedsRefresh = false;
            GameItem item = editor.Item;
            try
            {
                previewName.SetText(item.GetDisplayName() + " · " + selected.Id);
                int occupied = 0;
                for (int y = item.shape.minY; y <= item.shape.maxY; y++)
                    for (int x = item.shape.minX; x <= item.shape.maxX; x++) if (item.shape.Get(x, y) == 1) occupied++;
                previewInfo.SetText(Text.Get(editor.IsExisting ? "preview_existing" : "preview_new",
                    item.GetBaseValue(), item.unitCount, item.shape.globalWidth, item.shape.globalHeight, occupied));
                string description = ItemTooltipText.Plain(item is GameItemElement element && element.HasTooltip()
                    ? element.GetTooltip()?.nakedText : "");
                previewDescription.SetText(string.Join("\n", description.Split('\n').Take(4)));
                previewGraphic.Show(item, description);
                foreach (var field in moduleInputs)
                    SetInput(field.Value, field.Key == "value" ? item.unitValue.ToString(CultureInfo.InvariantCulture)
                        : item.GetTagReadonly(field.Key).GetInt().ToString(CultureInfo.InvariantCulture));
                if (editor.IsModule)
                {
                    SetIndex(effect1Input, effectIds?.IndexOf(ModuleEditing.Effect(item, 1)) ?? 0);
                    SetIndex(effect2Input, effectIds?.IndexOf(ModuleEditing.Effect(item, 2)) ?? 0);
                    SetIndex(shapeInput, editor.Shapes.FindIndex(shape => shape.Sprite == item.spritePath));
                }
                if (editor.IsLiquid)
                {
                    SetInput(capacityInput, WaterHelper.GetTotalCapacityML(item).ToString(CultureInfo.InvariantCulture));
                    SetInput(liquidAmountInput, WaterHelper.GetCurrentCapacityML(item).ToString(CultureInfo.InvariantCulture));
                }
                foreach (var field in propertyValues)
                    if (field.Key.input != null) SetInput(field.Key, field.Value());
                    else field.Key.SetText(field.Value());
                if (editor.IsGun) SetIndex(gunConditionInput, (int)GunFrameHelper.GetCondition(item));
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning("Item preview: " + error);
                SetStatus(Text.Get("preview_failed", error.Message));
            }
        }

        static void SetInput(CustomUIElement field, string value) => field?.input?.SetTextWithoutNotify(value);
        static void SetIndex(CustomUIElement field, int index)
        {
            if (field?.dropdown == null) return;
            field.dropdown.SetValueWithoutNotify(Math.Max(0, index));
            field.dropdown.RefreshShownValue();
        }
        static int ParseInt(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number : throw new InvalidOperationException(Text.Get("enter_a_valid_integer"));
        static long ParseLong(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
            ? number : throw new InvalidOperationException(Text.Get("enter_a_valid_integer"));
    }
}
