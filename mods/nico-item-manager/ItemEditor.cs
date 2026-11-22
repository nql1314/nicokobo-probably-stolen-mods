using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace NicoItemManager
{
    internal sealed partial class ItemEditor : IDisposable
    {
        internal GameItem Item { get; private set; }
        internal bool IsModule => Item.IsTag("MODULE_TAG") || Item.IsGameItemType("NODE");
        internal bool IsLiquid => Item.IsTag("LIQUID_CONTAINER_TAG");
        readonly Dictionary<string, TagState> original;
        readonly Dictionary<string, TagValueKind> selectedTagKinds = new Dictionary<string, TagValueKind>(StringComparer.Ordinal);
        bool shapeChanged, liquidEdited;
        internal readonly List<ModuleShape> Shapes;

        internal ItemEditor(string id) : this(id, null) { }

        ItemEditor(string id, GameItem source)
        {
            try
            {
                // Preview factories must not assign a run UID or notify permits.
                Item = ItemSpawner.CreateItem(id);
                if (!ItemSpawner.IsAlive(Item) || string.IsNullOrWhiteSpace(Item.identifier))
                    throw new InvalidOperationException(Text.Get("the_item_prototype_is_unavailable"));
                if (source == null) ItemSpawner.InitializeFoodStamp(Item);
                else LoadExisting(source);
                original = Item.state.dict.ToDictionary(pair => pair.Key, pair => pair.Value?.Clone());
                Shapes = IsModule ? ModuleEditing.ReadShapes(Item) : new List<ModuleShape>();
            }
            catch { Dispose(); throw; }
        }

        internal List<TagEdit> ReadTags() => ItemTags.ReadItem(Item, selectedTagKinds)
            .Where(edit => !IsManagedTag(edit.Key) && (!IsExisting || edit.Key != "IS_OWNED_TAG")).ToList();

        internal List<TagEdit> ReadNumericTags() => ReadTags().Where(edit => edit.Enabled
            && (edit.Kind == TagValueKind.Int || edit.Kind == TagValueKind.Float || edit.Kind == TagValueKind.Double)).ToList();

        bool IsManagedTag(string key) => IsModule && ModuleEditing.ManagedTags.Contains(key)
            || IsLiquid && (key == "LIQUID_CONTAINER_CAPACITY" || Liquid.Liquids.Any(liquid => liquid.currentPart == key))
            || ManagedPropertyTags().Contains(key);

        internal void ApplyTag(TagEdit edit)
        {
            if (IsExisting && edit.Key?.Trim() == "IS_OWNED_TAG")
                throw new InvalidOperationException(Text.Get("ownership_of_an_existing_item_cannot_be"));
            if (IsManagedTag((edit.Key ?? "").Trim()))
                throw new InvalidOperationException(Text.Get("use_the_specialized_fields_above_to_edit"));
            string error = ItemTags.ApplyTo(Item, new[] { edit });
            if (error != null) throw new InvalidOperationException(error);
            // Native zero values carry no type information; retain the user's choice for this editor session.
            selectedTagKinds[edit.Key.Trim()] = edit.Kind;
            if (IsModule) ModuleEffectHelper.ResetAllTempStat(Item);
            if (IsGun) GunHelper.UpdateCurrentGunStats(Item);
        }

        internal void SetModuleStat(string key, int value)
        {
            if (!IsModule || !ModuleEditing.Stats.Contains(key)) throw new InvalidOperationException("Invalid module field.");
            if (value < -999 || value > 999)
                throw new InvalidOperationException(Text.Get("module_stats_must_be_between_999_and"));
            Item.ModifyTag(key, tag => tag.Enable().SetInt(value));
            ModuleEffectHelper.ResetAllTempStat(Item);
        }

        internal void SetTier(int value)
        {
            if (value < 1 || value > 5) throw new InvalidOperationException(Text.Get("tier_must_be_1_5"));
            Item.ModifyTag("MODULE_TIER_INT", tag => tag.Enable().SetInt(value));
        }

        internal void SetValue(long value)
        {
            if (value < 0 || value > 9999999) throw new InvalidOperationException(Text.Get("value_must_be_0_9_999_999"));
            Item.SetValue(value);
            valueEdited = true;
        }

        internal void SetShape(int index)
        {
            if (IsExisting)
                throw new InvalidOperationException(Text.Get("shape_changes_are_unavailable_for_existing_items"));
            if (index < 0 || index >= Shapes.Count) throw new ArgumentOutOfRangeException(nameof(index));
            ModuleShape shape = Shapes[index];
            Item.SetSpriteAndShape(shape.Atlas, shape.Sprite);
            shapeChanged = true;
        }

        internal void SetCapacity(int ml)
        {
            if (ml < 1 || ml > 50000)
                throw new InvalidOperationException(Text.Get("capacity_must_be_1_50_000_ml"));
            Item.ModifyTag("LIQUID_CONTAINER_CAPACITY", tag => tag.Enable().SetInt(ml * 1000));
            WaterHelper.RemoveExcessLiquid(Item);
            RefreshLiquid();
        }

        internal void SetLiquidAmount(int ml)
        {
            int capacity = WaterHelper.GetTotalCapacityML(Item);
            if (ml < 0 || ml > capacity)
                throw new InvalidOperationException(Text.Get("amount_must_be_0_ml", capacity));
            int current = WaterHelper.GetCurrentCapacityUL(Item);
            if (ml == 0) { EmptyLiquid(); return; }
            if (current == 0)
                throw new InvalidOperationException(Text.Get("the_container_is_empty_inject_a_liquid"));
            // Scale every native component instead of replacing the mixture with water.
            var parts = Liquid.Liquids.Where(liquid => Item.GetTagReadonly(liquid.currentPart).GetInt() > 0).ToList();
            int remaining = ml * 1000;
            for (int i = 0; i < parts.Count; i++)
            {
                string key = parts[i].currentPart;
                int amount = i == parts.Count - 1 ? remaining
                    : (int)((long)Item.GetTagReadonly(key).GetInt() * ml * 1000 / current);
                remaining -= amount;
                Item.ModifyTag(key, tag => tag.SetInt(amount));
            }
            WaterHelper.RemoveExcessLiquid(Item);
            RefreshLiquid();
        }

        internal void EmptyLiquid()
        {
            WaterHelper.EmptyContainer(Item);
            RefreshLiquid();
        }

        internal void FillLiquid(int index)
        {
            if (index < 0 || index >= LiquidEditing.Presets.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (WaterHelper.GetTotalCapacityML(Item) <= 0)
                throw new InvalidOperationException(Text.Get("set_a_valid_capacity_first"));
            liquidEdited = true;
            WaterHelper.EmptyContainer(Item);
            LiquidEditing.Presets[index](Item);
            if (WaterHelper.GetCurrentCapacityUL(Item) <= 0)
                throw new InvalidOperationException(Text.Get("this_container_could_not_receive_the_selected"));
            RefreshLiquid();
        }

        void RefreshLiquid()
        {
            liquidEdited = true;
            LiquidEditing.Refresh(Item);
        }

        internal ItemConfiguration Capture()
        {
            var changes = ReadChanges();
            // Derived module stats and even zero liquid components must survive every
            // generated copy, including factories that start with a different state.
            if (IsModule)
                foreach (string key in ModuleEditing.ManagedTags) CaptureTag(changes, key);
            if (IsLiquid)
            {
                CaptureTag(changes, "LIQUID_CONTAINER_CAPACITY");
                foreach (Liquid liquid in Liquid.Liquids) CaptureTag(changes, liquid.currentPart);
            }
            foreach (string key in ManagedPropertyTags()) CaptureTag(changes, key);
            return new ItemConfiguration(changes, Item.unitValue, IsModule,
                ModuleEditing.Effect(Item, 1), ModuleEditing.Effect(Item, 2),
                shapeChanged, Item.spriteAtlasPath, Item.spritePath, liquidEdited, new ItemPropertyState(this),
                new Dictionary<string, TagValueKind>(selectedTagKinds));
        }

        internal void Restore(ItemConfiguration configuration)
        {
            configuration.ApplyTo(Item);
            shapeChanged = configuration.ShapeChanged;
            liquidEdited = configuration.LiquidEdited;
            GunConditionChanged = configuration.GunConditionChanged;
            selectedTagKinds.Clear();
            foreach (var pair in configuration.TagKinds) selectedTagKinds.Add(pair.Key, pair.Value);
        }

        Dictionary<string, TagState> ReadChanges()
        {
            var changes = new Dictionary<string, TagState>(StringComparer.Ordinal);
            foreach (string key in original.Keys.Union(Item.state.dict.Keys))
            {
                original.TryGetValue(key, out TagState before);
                Item.state.dict.TryGetValue(key, out TagState after);
                if (!Same(before, after)) changes[key] = after?.Clone();
            }
            return changes;
        }

        void CaptureTag(Dictionary<string, TagState> changes, string key)
        {
            Item.state.dict.TryGetValue(key, out TagState tag);
            changes[key] = tag?.Clone();
        }

        static bool Same(TagState a, TagState b) => a == null || b == null ? a == b
            : a.valueEnabled == b.valueEnabled && a.valueString == b.valueString && a.valueInt == b.valueInt
                && a.valueBool == b.valueBool && a.valueFloat == b.valueFloat && a.valueLong == b.valueLong
                && a.valueDouble == b.valueDouble;

        public void Dispose()
        {
            ItemSpawner.Release(Item);
            Item = null;
            Target = null;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class ItemConfiguration
    {
        [JsonProperty(Required = Required.Always)]
        readonly Dictionary<string, TagState> changes;
        [JsonProperty]
        readonly long value;
        [JsonProperty] readonly bool module;
        [JsonProperty] readonly bool shapeChanged;
        [JsonProperty] readonly bool liquidEdited;
        [JsonProperty] readonly string effect1;
        [JsonProperty] readonly string effect2;
        [JsonProperty] readonly string atlas;
        [JsonProperty] readonly string sprite;
        [JsonProperty(Required = Required.Always)]
        readonly ItemPropertyState properties;
        [JsonProperty(Required = Required.Always)] readonly Dictionary<string, TagValueKind> tagKinds;
        internal bool ShapeChanged => shapeChanged;
        internal bool LiquidEdited => liquidEdited;
        internal bool GunConditionChanged => properties.ConditionChanged;
        internal IReadOnlyDictionary<string, TagValueKind> TagKinds => tagKinds;

        [JsonConstructor]
        internal ItemConfiguration(Dictionary<string, TagState> changes, long value, bool module,
            string effect1, string effect2, bool shapeChanged, string atlas, string sprite, bool liquidEdited,
            ItemPropertyState properties, Dictionary<string, TagValueKind> tagKinds)
        {
            this.changes = changes;
            this.value = value;
            this.module = module;
            this.effect1 = effect1;
            this.effect2 = effect2;
            this.shapeChanged = shapeChanged;
            this.atlas = atlas;
            this.sprite = sprite;
            this.liquidEdited = liquidEdited;
            this.properties = properties;
            this.tagKinds = tagKinds;
        }

        internal void ApplyTo(GameItem target)
        {
            if (module)
            {
                // Remove mismatching slots before adding either requested effect.
                // Callbacks can change other item properties in addition to tags.
                for (int slot = 1; slot <= 2; slot++)
                    if (ModuleEditing.Effect(target, slot) != (slot == 1 ? effect1 : effect2))
                        ModuleEditing.SetEffect(target, slot, "");
                ModuleEditing.SetEffect(target, 1, effect1);
                ModuleEditing.SetEffect(target, 2, effect2);
            }
            if (shapeChanged) target.SetSpriteAndShape(atlas, sprite);
            foreach (var pair in changes)
                if (pair.Value == null) target.state.dict.Remove(pair.Key);
                else target.state.dict[pair.Key] = pair.Value.Clone();
            ApplyPurity(target, changes);
            target.ValidateShapeState();
            if (module) ModuleEffectHelper.ResetAllTempStat(target);
            if (liquidEdited) LiquidEditing.Refresh(target);
            target.SetValue(value);
            properties.ApplyTo(target);
            // Native feature refreshes may calculate price modifiers from unitValue;
            // keep the final user-edited value after those refreshes as well.
            target.SetValue(value);
        }

        internal static void ApplyPurity(GameItem target, Dictionary<string, TagState> changes)
        {
            if (changes.TryGetValue(IngotPurityHelper.INGOT_PURITY_TAG, out TagState purity))
            {
                ItemFeature feature = target.FindItemFeatureByID(IngotPurityHelper.INGOT_PURITY_TAG);
                if (purity != null && purity.valueEnabled && !string.IsNullOrEmpty(purity.valueString))
                {
                    if (feature == null) target.AddItemFeature(IngotPurityHelper.FeaturePurity());
                    IngotPurityHelper.SetPurity(target, purity.valueString);
                }
                else if (feature != null) target.RemoveItemFeature(feature);
            }
        }
    }
}
