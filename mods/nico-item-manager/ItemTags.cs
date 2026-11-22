using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NicoItemManager
{
    internal enum TagValueKind { String, Int, Bool, Float, Double }

    internal sealed class TagEdit
    {
        internal string Key;
        internal TagValueKind Kind;
        internal string Text = "";
        internal bool BoolValue;
        internal bool Enabled = true;
        internal bool Removed;
        internal bool IsNew;
        internal TagValueKind OriginalKind;
        internal string OriginalText;
        internal bool OriginalBool;
        internal bool OriginalEnabled;
        internal bool IsPurity => Key == IngotPurityHelper.INGOT_PURITY_TAG;
        internal bool ValueChanged => IsNew || Kind != OriginalKind
            || (Kind == TagValueKind.Bool ? BoolValue != OriginalBool : Text != OriginalText);
        internal bool IsDirty => Removed || Enabled != OriginalEnabled || ValueChanged;
        internal string DisplayValue => Kind == TagValueKind.Bool ? (BoolValue ? "true" : "false") : Text;
    }

    internal static class ItemTags
    {
        internal static readonly string[] PurityGrades = {
            "INGOT_PURITY_VERY_LOW", "INGOT_PURITY_LOW", "INGOT_PURITY_FAIR",
            "INGOT_PURITY_HIGH", "INGOT_PURITY_VERY_HIGH", "INGOT_PURITY_PERFECT"
        };
        internal static string PurityLabel(int index) =>
            LocHelper.GetLocalizedMechanic("mech_condition_" + PurityGrades[index].Substring("INGOT_PURITY_".Length).ToLowerInvariant() + "_purity");
        internal static string KindLabel(TagValueKind kind) => kind switch
        {
            TagValueKind.Int => Text.Get("integer"),
            TagValueKind.Bool => Text.Get("boolean"),
            TagValueKind.Float => Text.Get("float"),
            TagValueKind.Double => Text.Get("double"),
            _ => Text.Get("string")
        };

        internal static string ValueText(TagState tag, TagValueKind kind) => kind switch
        {
            TagValueKind.Int => tag.valueInt.ToString(CultureInfo.InvariantCulture),
            TagValueKind.Float => tag.valueFloat.ToString("R", CultureInfo.InvariantCulture),
            TagValueKind.Double => tag.valueDouble.ToString("R", CultureInfo.InvariantCulture),
            _ => tag.valueString ?? ""
        };

        internal static List<TagEdit> ReadItem(GameItem item, IReadOnlyDictionary<string, TagValueKind> selectedKinds = null)
        {
            var result = new List<TagEdit>();
            if (item?.state?.dict == null) return result;
            foreach (var pair in item.state.dict.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                TagState tag = pair.Value;
                if (tag == null) continue;
                // Prefer verified native types, including zero values. Unknown tags
                // still need a best-effort guess because TagState has no type field.
                TagValueKind kind = selectedKinds != null && selectedKinds.TryGetValue(pair.Key, out TagValueKind selectedKind) ? selectedKind
                    : ItemTagDescriptions.Find(item, pair.Key)?.ValueKind is TagValueKind nativeKind ? nativeKind
                    : pair.Key.EndsWith("_INT", StringComparison.Ordinal) ? TagValueKind.Int
                    : pair.Key.EndsWith("_FLOAT", StringComparison.Ordinal) ? TagValueKind.Float
                    : pair.Key.EndsWith("_DOUBLE", StringComparison.Ordinal) ? TagValueKind.Double
                    : !string.IsNullOrEmpty(tag.valueString) ? TagValueKind.String
                    : tag.valueBool ? TagValueKind.Bool
                    : tag.valueInt != 0 ? TagValueKind.Int
                    : tag.valueFloat != 0 ? TagValueKind.Float
                    : tag.valueDouble != 0 ? TagValueKind.Double : TagValueKind.String;
                string value = ValueText(tag, kind);
                result.Add(new TagEdit { Key = pair.Key, Kind = kind, OriginalKind = kind,
                    Text = value, OriginalText = value, BoolValue = tag.valueBool, OriginalBool = tag.valueBool,
                    Enabled = tag.valueEnabled, OriginalEnabled = tag.valueEnabled });
            }
            return result;
        }

        internal static string ApplyTo(GameItem item, IReadOnlyList<TagEdit> edits)
        {
            if (edits == null) return null;
            try
            {
                foreach (TagEdit edit in edits)
                {
                    if (edit == null || !edit.IsDirty) continue;
                    if (string.IsNullOrWhiteSpace(edit.Key)) throw new InvalidOperationException(Text.Get("tag_key_cannot_be_empty"));
                    ItemTagDescription description = ItemTagDescriptions.Find(item, edit.Key.Trim());
                    if (!edit.Removed && description?.ValueKind is TagValueKind requiredKind && edit.Kind != requiredKind)
                        throw new InvalidOperationException(Text.Get("requires_the_type", description.Name, KindLabel(requiredKind)));
                    if (!edit.Removed && edit.Kind == TagValueKind.Int)
                    {
                        if (!int.TryParse(edit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                            throw new InvalidOperationException(Text.Get("invalid_integer", edit.Text));
                        if (description != null && (intValue < description.MinimumInt || intValue > description.MaximumInt))
                            throw new InvalidOperationException(Text.Get("must_be_between_and", description.Name, description.MinimumInt, description.MaximumInt));
                    }
                    if (!edit.Removed && edit.Kind == TagValueKind.Float && (!float.TryParse(edit.Text,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue)
                        || float.IsNaN(floatValue) || float.IsInfinity(floatValue)))
                        throw new InvalidOperationException(Text.Get("invalid_finite_float", edit.Text));
                    if (!edit.Removed && edit.Kind == TagValueKind.Double && (!double.TryParse(edit.Text,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue)
                        || double.IsNaN(doubleValue) || double.IsInfinity(doubleValue)))
                        throw new InvalidOperationException(Text.Get("invalid_finite_double", edit.Text));
                    if (edit.IsPurity && !edit.Removed && edit.Enabled && !string.IsNullOrWhiteSpace(edit.Text)
                        && (edit.Kind != TagValueKind.String || !PurityGrades.Contains(edit.Text.Trim())))
                        throw new InvalidOperationException(Text.Get("choose_a_valid_purity_grade"));
                }
                bool changed = false;
                foreach (TagEdit edit in edits)
                {
                    if (edit == null || !edit.IsDirty) continue;
                    changed = true;
                    string key = edit.Key.Trim();
                    if (edit.IsPurity)
                    {
                        if (edit.Removed || !edit.Enabled || string.IsNullOrWhiteSpace(edit.Text))
                        {
                            item.DisableTag(key, true);
                            item.DisableTag(IngotPurityHelper.INGOT_PURITY_FAKE_TAG, true);
                            ItemFeature feature = item.FindItemFeatureByID(key);
                            if (feature != null) item.RemoveItemFeature(feature);
                            continue;
                        }
                        if (item.FindItemFeatureByID(key) == null)
                            item.AddItemFeature(IngotPurityHelper.FeaturePurity());
                        IngotPurityHelper.SetPurity(item, edit.Text.Trim());
                        continue;
                    }
                    if (edit.Removed)
                    {
                        item.DisableTag(key, true);
                        continue;
                    }
                    if (!item.state.dict.TryGetValue(key, out TagState tag) || tag == null)
                        item.state.dict[key] = tag = new TagState(key, key);
                    if (edit.ValueChanged)
                    {
                        tag.Reset();
                        switch (edit.Kind)
                        {
                            case TagValueKind.Int: tag.SetInt(int.Parse(edit.Text, CultureInfo.InvariantCulture)); break;
                            case TagValueKind.Bool: tag.SetBool(edit.BoolValue); break;
                            case TagValueKind.Float: tag.SetFloat(float.Parse(edit.Text, NumberStyles.Float, CultureInfo.InvariantCulture)); break;
                            case TagValueKind.Double: tag.SetDouble(double.Parse(edit.Text, NumberStyles.Float, CultureInfo.InvariantCulture)); break;
                            default: tag.SetString(edit.Text ?? ""); break;
                        }
                    }
                    tag.SetEnabled(edit.Enabled);
                }
                if (changed) item.ValidateShapeState();
                return null;
            }
            catch (Exception error)
            {
                NicoItemManagerMod.Log?.Warning($"Tag edits rejected: {error}");
                return Text.Get("could_not_apply_tags", error.Message);
            }
        }
    }
}
