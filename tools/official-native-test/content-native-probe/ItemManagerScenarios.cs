using System.Collections;
using System.Globalization;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const string ManagerInt = "NATIVE_PROBE_INT";
    private const string ManagerFloat = "NATIVE_PROBE_FLOAT";
    private const string ManagerDouble = "NATIVE_PROBE_DOUBLE";
    private const string ManagerDisabled = "NATIVE_PROBE_DISABLED_FLOAT";
    private const string ManagerFlag = "NATIVE_PROBE_BOOL";
    private const string ManagerLabel = "NATIVE_PROBE_LABEL";
    private static readonly string[] ManagerFixtureKeys = { ManagerInt, ManagerFloat, ManagerDouble, ManagerDisabled, ManagerFlag, ManagerLabel };
    private static Type ManagerType(string name) => Reflect.Type("NicoItemManager." + name, "NicoItemManager");
    private static object ManagerEditor(object panel) => Reflect.Get(panel, "editor") ?? throw new InvalidOperationException("Manager did not create its real editor");
    private static GameItem ManagerPreview(object panel) => (GameItem)Reflect.Get(ManagerEditor(panel), "Item")!;

    internal IEnumerable<object?> ItemManager()
    {
        R.phase = "manager-real-panel-and-numeric-fields";
        var candidate = ModLoader.Mods.Single(mod => mod.Manifest.ID == "nicokobo.item-manager");
        R.Check("manager.loaded-version", "1.3.2", candidate.Manifest.ModVersion, candidate.Manifest.ModVersion == "1.3.2");
        string expectedLocale = O.Family == "manageren" ? "en" : "zh";
        string? actualLocale = ModHelper.GetCurrentLocale()?.Identifier.Code;
        R.environment["managerLocale"] = actualLocale;
        R.Check("manager.expected-process-locale", expectedLocale, actualLocale, actualLocale == expectedLocale,
            "Language was selected through the main-menu dropdown before InventoryScene; this scenario never changes locale in the running store.");
        object owner = Reflect.Get(ManagerType("NicoItemManagerMod"), "Active") ?? throw new InvalidOperationException("No active Item Manager");
        object catalog = Reflect.Get(owner, "Catalog")!;
        while (!(bool)Reflect.Get(catalog, "Ready")!) yield return null;
        var storeTrash = EmporiumEntry.Instance.trashcan;
        R.Check("manager.complete-native-catalog-preserves-fixed-trash-endpoint", "The original store trashcan owns the endpoint after every actual registered factory was inspected", new
        { scanned = Reflect.Int(catalog, "Scanned"), total = Reflect.Int(catalog, "Total"), originalUid = storeTrash.uniqueId,
            actualOwner = EmporiumEntry.Instance.trashcanInvElement?.GetParentItem()?.uniqueId },
            ReferenceEquals(EmporiumEntry.Instance.trashcanInvElement, storeTrash.contentWindow.childElement) && ReferenceEquals(EmporiumEntry.Instance.trashcanInvElement.GetParentItem(), storeTrash));
        object panel = Reflect.Get(owner, "Panel")!;
        var saved = new JArray();
        var generated = new List<GameItem>();
        GameItem existing = Spawn("energy_credit");
        Retain(existing);
        JToken existingBefore = ManagerAllTags(existing);
        var inventoryBeforeEditing = SnapshotItems();
        Reflect.Call(panel, "Toggle");
        try
        {
            R.Check("manager.real-panel-visible", true, Reflect.Get(panel, "IsVisible"), (bool)Reflect.Get(panel, "IsVisible")!);
            ManagerOpen(panel, catalog, "current_converter");
            R.Check("manager.numeric-default-without-advanced", false, Reflect.Get(panel, "showAdvanced"), !(bool)Reflect.Get(panel, "showAdvanced")!);
            ManagerAssertDescription(panel, "CURRENT_CONVERTER_UPGRADE_COUNT_INT", "换流器升级次数", "Converter upgrade count",
                "换流器效率升级次数，原生最多 5 次。", "Converter efficiency upgrades; native upgrading allows up to five.");
            ManagerAssertDescription(panel, "CURRENT_CONVERTER_RATE_FLOAT", "供电费用（信用点/kJ）", "Energy cost (credits/kJ)",
                "每提供 1 kJ 所需的信用点，越低越省；原生对非正值按 1.5 处理。", "Credits charged per 1 kJ supplied. Lower costs less; native logic treats nonpositive values as 1.5.");
            ManagerAssertNumber(panel, "CURRENT_CONVERTER_UPGRADE_COUNT_INT", "Int", 0, "native-int-zero-visible");
            ManagerAssertNumber(panel, "CURRENT_CONVERTER_RATE_FLOAT", "Float", 1.5f, "native-float-visible");
            ManagerEditNumber(panel, "CURRENT_CONVERTER_UPGRADE_COUNT_INT", "-7", "Int", -7, "native-negative-int");
            ManagerEditNumber(panel, "CURRENT_CONVERTER_RATE_FLOAT", "-6.25e-2", "Float", -0.0625f, "native-negative-scientific-float");

            ManagerOpen(panel, catalog, "tilapia");
            ManagerAssertDescription(panel, "FISH_HEALTH_TAG", "鱼类健康值", "Fish health",
                "当前健康值，上限由鱼种定义。", "Current health; the maximum is defined by the species.");
            ManagerAssertNumber(panel, "FISH_HEALTH_TAG", "Float", 100f, "native-unsuffixed-float");
            ManagerEditNumber(panel, "FISH_HEALTH_TAG", "0", "Float", 0f, "unsuffixed-zero-keeps-session-type");
            ManagerEditNumber(panel, "FISH_HEALTH_TAG", "8.7125e1", "Float", 87.125f, "unsuffixed-edit-after-zero");

            ManagerOpen(panel, catalog, "water_jug");
            ManagerAssertDescription(panel, "CONTAINER_TEMP_TAG", "容器温度（°C）", "Container temperature (°C)",
                "容器内物质的原生温度，用于温度反应与冷却。", "Native contents temperature used in thermal reactions and cooling.");
            ManagerAssertNumber(panel, "CONTAINER_TEMP_TAG", "Double", 24d, "native-double-visible");
            ManagerEditNumber(panel, "CONTAINER_TEMP_TAG", "2.5123456789012345e1", "Double", 25.123456789012345d, "native-double-roundtrip");
            R.Check("manager.preview-does-not-touch-existing-inventory", inventoryBeforeEditing, SnapshotItems(),
                JToken.DeepEquals(inventoryBeforeEditing, SnapshotItems()) && JToken.DeepEquals(existingBefore, ManagerAllTags(existing)));
            var water = ManagerSpawnButtons(panel, 1, new[] { "CONTAINER_TEMP_TAG" }, "native-water-double-x1");
            generated.AddRange(water);
            foreach (GameItem item in water) saved.Add(ManagerReceipt(item, new[] { "CONTAINER_TEMP_TAG" }));

            ManagerOpen(panel, catalog, "energy_credit");
            GameItem preview = ManagerPreview(panel);
            // These are explicit fixture fields on the real editor's real native
            // battery prototype, not replacements for its parser or UI callbacks.
            preview.ModifyTag(ManagerInt, tag => tag.Enable().SetInt(0));
            preview.ModifyTag(ManagerFloat, tag => tag.Enable().SetFloat(0.12345679f));
            preview.ModifyTag(ManagerDouble, tag => tag.Enable().SetDouble(1.2345678901234567d));
            preview.ModifyTag(ManagerDisabled, tag => tag.Disable().SetFloat(1.25f));
            preview.ModifyTag(ManagerFlag, tag => tag.Enable().SetBool(true));
            preview.ModifyTag(ManagerLabel, tag => tag.Enable().SetString("A"));
            R.environment["managerExplicitFixture"] = new { item = preview.identifier, keys = ManagerFixtureKeys,
                scope = "Added once to the actual ItemEditor prototype before constructing controls; production UI, parser, Capture, Spawn and ES3 are unchanged" };
            Reflect.Call(panel, "BuildEditor");
            Reflect.Call(panel, "RefreshEditor");
            ManagerAssertNumber(panel, ManagerInt, "Int", 0, "fixture-zero-int-visible");
            ManagerAssertNumber(panel, ManagerFloat, "Float", 0.12345679f, "fixture-float-roundtrip-read");
            ManagerAssertNumber(panel, ManagerDouble, "Double", 1.2345678901234567d, "fixture-double-roundtrip-read");
            ManagerAssertUnknownLabel(panel, ManagerDouble, 1, "outer");
            string[] hidden = { "power_source_item_energy", "power_source_item_max_energy", "power_source_item_bandwidth", ManagerDisabled, ManagerFlag, ManagerLabel };
            string[] numericKeys = Reflect.Each(Reflect.Call(ManagerEditor(panel), "ReadNumericTags")).Select(tag => Reflect.Str(tag, "Key")).ToArray();
            R.Check("manager.no-specialized-disabled-or-nonnumeric-duplicates", hidden,
                new { numericKeys, unexpectedRows = hidden.Where(key => ManagerRows(panel, key).Any()).ToArray() },
                hidden.All(key => !numericKeys.Contains(key) && !ManagerRows(panel, key).Any()) && numericKeys.Distinct().Count() == numericKeys.Length);

            ManagerSetAdvanced(panel, true);
            ManagerAssertUnknownLabel(panel, ManagerDouble, 2, "outer-and-advanced");
            ManagerAssertUnknownLabel(panel, ManagerLabel, 1, "advanced-string");
            foreach (string key in new[] { ManagerInt, ManagerFloat, ManagerDouble })
            {
                var rows = ManagerRows(panel, key);
                Transform advanced = rows.Single(row => row.GetComponentsInChildren<TMP_Dropdown>().Length > 0);
                R.Check("manager.advanced-numeric-readonly." + key, new { rows = 2, inputs = 1, advancedInputs = 0 },
                    new { rows = rows.Length, inputs = rows.Sum(row => row.GetComponentsInChildren<TMP_InputField>().Length), advancedInputs = advanced.GetComponentsInChildren<TMP_InputField>().Length },
                    rows.Length == 2 && rows.Sum(row => row.GetComponentsInChildren<TMP_InputField>().Length) == 1 && advanced.GetComponentsInChildren<TMP_InputField>().Length == 0);
            }

            R.phase = "manager-advanced-existing-control-roundtrip";
            ManagerAdvancedRoundtrips(panel);
            ManagerSetAdvanced(panel, false);
            ManagerEditNumber(panel, ManagerInt, "-17", "Int", -17, "fixture-negative-int");
            ManagerEditNumber(panel, ManagerFloat, "-1.2345679e-1", "Float", -0.12345679f, "fixture-negative-scientific-float");
            ManagerEditNumber(panel, ManagerDouble, "-1.2345678901234567e0", "Double", -1.2345678901234567d, "fixture-negative-scientific-double");
            foreach (var invalid in new[] {
                (ManagerInt, "2147483648"), (ManagerInt, "1e2"), (ManagerInt, "bad"),
                (ManagerFloat, "bad"), (ManagerFloat, "NaN"), (ManagerFloat, "Infinity"), (ManagerFloat, "-Infinity"), (ManagerFloat, "3.5e38"),
                (ManagerDouble, "bad"), (ManagerDouble, "NaN"), (ManagerDouble, "Infinity"), (ManagerDouble, "-Infinity"), (ManagerDouble, "1e309") })
                ManagerRejectNumber(panel, invalid.Item1, invalid.Item2);

            R.phase = "manager-real-spawn-x1-x5-and-independent-values";
            var first = ManagerSpawnButtons(panel, 1, ManagerFixtureKeys, "fixture-x1");
            generated.AddRange(first);
            foreach (GameItem item in first) saved.Add(ManagerReceipt(item, ManagerFixtureKeys));
            ManagerEditNumber(panel, ManagerFloat, "6.25e-1", "Float", 0.625f, "next-batch-float");
            ManagerEditNumber(panel, ManagerDouble, "1.2345678901234567", "Double", 1.2345678901234567d, "next-batch-double");
            var five = ManagerSpawnButtons(panel, 5, ManagerFixtureKeys, "fixture-x5");
            generated.AddRange(five);
            foreach (GameItem item in five) saved.Add(ManagerReceipt(item, ManagerFixtureKeys));
            ManagerEditNumber(panel, ManagerFloat, "0.5", "Float", 0.5f, "post-spawn-preview-only-edit");
            bool independent = generated.All(item => !ReferenceEquals(item, preview) && !ReferenceEquals(item.state, preview.state)) &&
                first.Concat(five).All(item => ManagerFixtureKeys.All(key => !ReferenceEquals(item.GetTagReadonly(key), preview.GetTagReadonly(key)))) &&
                five.SelectMany((item, index) => five.Skip(index + 1).Select(other => (item, other)))
                    .All(pair => ManagerFixtureKeys.All(key => !ReferenceEquals(pair.item.GetTagReadonly(key), pair.other.GetTagReadonly(key))));
            foreach (JObject receipt in saved)
            {
                GameItem item = generated.Single(value => value.uniqueId == (int)receipt["uid"]!);
                R.Check("manager.generated-stable-after-later-preview." + item.uniqueId, receipt, ManagerReceipt(item, ((JObject)receipt["tags"]!).Properties().Select(property => property.Name)),
                    JToken.DeepEquals(receipt, ManagerReceipt(item, ((JObject)receipt["tags"]!).Properties().Select(property => property.Name))));
            }
            R.Check("manager.preview-spawn-existing-object-independence", true,
                new { independent, existingUnchanged = JToken.DeepEquals(existingBefore, ManagerAllTags(existing)), generatedUids = generated.Select(item => item.uniqueId).ToArray() },
                independent && JToken.DeepEquals(existingBefore, ManagerAllTags(existing)) && generated.All(item => item.uniqueId != existing.uniqueId));
            R.persistence["manager"] = new { items = saved, existingControl = ManagerReceipt(existing, existing.state.dict.Keys),
                source = "Real ManagerPanel TMP_InputField/Toggle/Button UnityEvents -> ItemEditor -> ItemSpawner -> native slot; actual SaveGame follows" };
            R.Check("manager.process-locale-kept", expectedLocale, ModHelper.GetCurrentLocale()?.Identifier.Code,
                ModHelper.GetCurrentLocale()?.Identifier.Code == expectedLocale,
                "Actual labels, bilingual tooltips, units and raw keys were tested in this process's selected language; no inventory-scene locale switch or restoration.");
        }
        finally { Reflect.Call(panel, "Close"); }
        yield return null; yield return null;
        R.Check("manager.panel-closed-before-save", false, Reflect.Get(panel, "IsVisible"), !(bool)Reflect.Get(panel, "IsVisible")!);
        R.Skip("manager.physical-ui-and-full-night", "Programmatic real UnityEvent callbacks only; keyboard/mouse text entry, visual layout, ten open/close cycles and full EndNight were not executed", false);
    }

    private void ManagerOpen(object panel, object catalog, string id)
    {
        object entry = Reflect.Each(Reflect.Get(catalog, "Entries")).Single(value => Reflect.Str(value, "Id") == id);
        Reflect.Call(panel, "OpenEditor", entry);
        Reflect.Call(panel, "Rebuild");
        if (ManagerPreview(panel).identifier != id) throw new InvalidOperationException("Real manager selected a different item: " + id);
    }

    private static CustomUITooltipTrigger[] ManagerTagLabels(object panel, string key)
    {
        var root = (RectTransform)Reflect.Get(panel, "editorRoot")!;
        // The actual product label carries the raw key in its tooltip in both
        // languages; its visible text may now be a localized description.
        return root.GetComponentsInChildren<CustomUITooltipTrigger>()
            .Where(label => (label.tooltipText ?? "").EndsWith("\nTag: " + key, StringComparison.Ordinal)).ToArray();
    }
    private static Transform[] ManagerRows(object panel, string key) => ManagerTagLabels(panel, key)
        .Select(label => label.GetComponentInParent<HorizontalLayoutGroup>()?.transform).Where(row => row != null).Cast<Transform>().Distinct().ToArray();

    private void ManagerAssertDescription(object panel, string key, string chinese, string english, string helpChinese, string helpEnglish)
    {
        object? description = Reflect.Call(ManagerType("ItemTagDescriptions"), "Find", ManagerPreview(panel), key);
        var metadata = new { chinese = description == null ? null : Reflect.Str(description, "ChineseName"),
            english = description == null ? null : Reflect.Str(description, "EnglishName"),
            helpChinese = description == null ? null : Reflect.Str(description, "ChineseHelp"),
            helpEnglish = description == null ? null : Reflect.Str(description, "EnglishHelp") };
        string expectedLocale = O.Family == "manageren" ? "en" : "zh";
        string suffix = "." + expectedLocale;
        R.Check("manager.tag-description-bilingual-metadata." + key + suffix, new { chinese, english, helpChinese, helpEnglish }, metadata,
            metadata.chinese == chinese && metadata.english == english && metadata.helpChinese == helpChinese && metadata.helpEnglish == helpEnglish,
            "Real installed ItemTagDescriptions.Find on the selected native item; expected names/help are independent fixed contract strings");
        bool zh = expectedLocale == "zh";
        string expectedLabel = zh ? chinese : english;
        string expectedTooltip = chinese + " / " + english + "\n" + (zh ? helpChinese : helpEnglish) + "\nTag: " + key;
        var labels = ManagerTagLabels(panel, key);
        var actual = labels.Select(label => new { name = label.GetComponentInChildren<TextMeshProUGUI>()?.text, tooltip = label.tooltipText }).ToArray();
        R.Check("manager.tag-description-rendered-label-tooltip." + key + suffix, new { name = expectedLabel, tooltip = expectedTooltip }, actual,
            actual.Length == 1 && actual[0].name == expectedLabel && actual[0].tooltip == expectedTooltip,
            "Actual built CustomUI label and CustomUITooltipTrigger text; this check does not call the product Name/Tooltip methods to generate its expectation");
    }
    private void ManagerAssertUnknownLabel(object panel, string key, int expectedRows, string scope)
    {
        string expectedLocale = O.Family == "manageren" ? "en" : "zh";
        bool zh = expectedLocale == "zh";
        string expectedTooltip = (zh ? "尚未确认此标签在该物品上的含义。" : "This tag's meaning on this item has not been confirmed.") + "\nTag: " + key;
        object? description = Reflect.Call(ManagerType("ItemTagDescriptions"), "Find", ManagerPreview(panel), key);
        var actual = ManagerTagLabels(panel, key).Select(label => new { name = label.GetComponentInChildren<TextMeshProUGUI>()?.text, tooltip = label.tooltipText }).ToArray();
        R.Check("manager.unknown-tag-key-retained." + scope + "." + expectedLocale, new { name = key, tooltip = expectedTooltip, rows = expectedRows },
            new { hasDescription = description != null, labels = actual, originalKeyStillPresent = ManagerPreview(panel).state.dict.ContainsKey(key) },
            description == null && actual.Length == expectedRows && actual.All(label => label.name == key && label.tooltip == expectedTooltip) && ManagerPreview(panel).state.dict.ContainsKey(key),
            "Explicit NATIVE_PROBE fixture key remains visible/editable through the product's real unknown-description branch");
    }
    private static TMP_InputField ManagerInput(object panel, string key, bool advanced = false) => ManagerRows(panel, key)
        .Single(row => (row.GetComponentsInChildren<TMP_Dropdown>().Length > 0) == advanced).GetComponentsInChildren<TMP_InputField>().Single();
    private static void ManagerSubmit(object panel, TMP_InputField input, string value)
    {
        input.SetTextWithoutNotify(value);
        input.onEndEdit.Invoke(value);
        // Execute the real requested refresh. Do not force BuildEditor here:
        // rebuilding every row would hide captured-TagEdit callback regressions.
        Reflect.Call(panel, "Rebuild");
    }
    private static void ManagerSetAdvanced(object panel, bool enabled)
    {
        var root = (RectTransform)Reflect.Get(panel, "editorRoot")!;
        Toggle toggle = root.GetComponentsInChildren<Toggle>().Single(value => value.GetComponentsInChildren<TextMeshProUGUI>()
            .Any(text => text.text is "高级标签" or "Advanced tags"));
        toggle.SetIsOnWithoutNotify(enabled); toggle.onValueChanged.Invoke(enabled);
        Reflect.Call(panel, "Rebuild");
    }
    private static string ManagerNumberText(object value) => value is float single ? single.ToString("R", CultureInfo.InvariantCulture)
        : value is double number ? number.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture)!;
    private void ManagerAssertNumber(object panel, string key, string kind, object expected, string name)
    {
        GameItem item = ManagerPreview(panel);
        var tag = item.GetTagReadonly(key);
        object field = Reflect.Each(Reflect.Call(ManagerEditor(panel), "ReadNumericTags")).Single(value => Reflect.Str(value, "Key") == key);
        string text = ManagerInput(panel, key).text;
        bool correct = kind switch { "Int" => expected is int integer && tag.valueInt == integer, "Float" => expected is float single && tag.valueFloat == single,
            "Double" => expected is double number && tag.valueDouble == number, _ => false };
        R.Check("manager." + name, new { key, kind, value = ManagerNumberText(expected), enabled = true },
            new { key, kind = Reflect.Str(field, "Kind"), text, state = ManagerTag(tag), previewStatus = Reflect.Str(panel, "status") },
            correct && tag.valueEnabled && Reflect.Str(field, "Kind") == kind && text == ManagerNumberText(expected),
            "Actual enabled outer InputField and real ItemEditor prototype; expected numbers are independent constants");
    }
    private void ManagerEditNumber(object panel, string key, string value, string kind, object expected, string name)
    {
        ManagerSubmit(panel, ManagerInput(panel, key), value);
        ManagerAssertNumber(panel, key, kind, expected, name);
        var graphic = Reflect.Get(panel, "previewGraphic")!;
        var image = (Image)Reflect.Get(graphic, "baseImage")!;
        var info = (CustomUIElement)Reflect.Get(panel, "previewInfo")!;
        R.Check("manager.preview-refresh." + name, "Accepted model, input and real preview refreshed without error",
            new { pending = Reflect.Get(panel, "previewNeedsRefresh"), status = Reflect.Str(panel, "status"), sprite = image.sprite?.name, info = info.text.text },
            !(bool)Reflect.Get(panel, "previewNeedsRefresh")! && Reflect.Str(panel, "status").Length == 0 && image.enabled && image.sprite != null && !string.IsNullOrEmpty(info.text.text));
    }
    private void ManagerRejectNumber(object panel, string key, string value)
    {
        GameItem item = ManagerPreview(panel);
        JToken before = ManagerAllTags(item);
        string acceptedText = ManagerInput(panel, key).text;
        ManagerSubmit(panel, ManagerInput(panel, key), value);
        bool unchanged = JToken.DeepEquals(before, ManagerAllTags(item));
        string status = Reflect.Str(panel, "status");
        string actualText = ManagerInput(panel, key).text;
        R.Check("manager.invalid." + key + "." + value, new { rejected = true, unchanged = true, text = acceptedText },
            new { unchanged, text = actualText, status }, unchanged && status.Length > 0 && actualText == acceptedText,
            "Real onEndEdit -> ApplyEditor -> ItemTags validation; every prototype TagState slot compared before/after");
    }
    private void ManagerAdvancedRoundtrips(object panel)
    {
        GameItem item = ManagerPreview(panel);
        TMP_InputField label = ManagerInput(panel, ManagerLabel, true);
        ManagerSubmit(panel, label, "B");
        bool firstLabel = item.GetTagReadonly(ManagerLabel).valueString == "B";
        TMP_InputField currentLabel = ManagerInput(panel, ManagerLabel, true);
        ManagerSubmit(panel, currentLabel, "A");
        R.Check("manager.advanced-string-A-B-A", new { value = "A" },
            new { firstLabel, actual = item.GetTagReadonly(ManagerLabel).valueString, displayed = ManagerInput(panel, ManagerLabel, true).text, sameLiveControl = ReferenceEquals(label, currentLabel) },
            firstLabel && item.GetTagReadonly(ManagerLabel).valueString == "A" && ManagerInput(panel, ManagerLabel, true).text == "A");

        Transform boolRow = ManagerRows(panel, ManagerFlag).Single();
        Toggle flag = boolRow.GetComponentsInChildren<Toggle>().Single(value => value.GetComponentsInChildren<TextMeshProUGUI>().Any(text => text.text == "true"));
        flag.SetIsOnWithoutNotify(false); flag.onValueChanged.Invoke(false); Reflect.Call(panel, "Rebuild");
        bool firstBool = !item.GetTagReadonly(ManagerFlag).valueBool;
        flag.SetIsOnWithoutNotify(true); flag.onValueChanged.Invoke(true); Reflect.Call(panel, "Rebuild");
        R.Check("manager.advanced-bool-true-false-true", true,
            new { firstBool, actual = item.GetTagReadonly(ManagerFlag).valueBool, displayed = flag.isOn }, firstBool && item.GetTagReadonly(ManagerFlag).valueBool && flag.isOn);

        TMP_InputField disabled = ManagerInput(panel, ManagerDisabled, true);
        ManagerSubmit(panel, disabled, "2.5");
        bool firstFloat = item.GetTagReadonly(ManagerDisabled).valueFloat == 2.5f;
        ManagerSubmit(panel, disabled, "1.25");
        R.Check("manager.advanced-disabled-float-roundtrip", new { value = 1.25f, enabled = false },
            new { firstFloat, actual = item.GetTagReadonly(ManagerDisabled).valueFloat, enabled = item.GetTagReadonly(ManagerDisabled).valueEnabled, displayed = disabled.text },
            firstFloat && item.GetTagReadonly(ManagerDisabled).valueFloat == 1.25f && !item.GetTagReadonly(ManagerDisabled).valueEnabled && disabled.text == "1.25");
    }

    private GameItem[] ManagerSpawnButtons(object panel, int count, string[] keys, string name)
    {
        GameItem preview = ManagerPreview(panel);
        JObject expectedTags = ManagerTags(preview, keys);
        var before = Store.FindAllItem().Select(item => item.uniqueId).ToHashSet();
        var buttons = Reflect.Each(Reflect.Get(panel, "editorSpawnButtons")).Cast<CustomUIElement>().ToArray();
        CustomUIElement button = buttons[count == 1 ? 0 : 1];
        bool ready = button.button != null && button.button.interactable;
        if (!ready) throw new InvalidOperationException("Real manager Spawn button disabled");
        button.button!.onClick.Invoke();
        var created = Store.FindAllItem().Where(item => !before.Contains(item.uniqueId) && item.identifier == preview.identifier).Distinct().OrderBy(item => item.uniqueId).ToArray();
        bool valid = created.Length == count && created.All(item => item.uniqueId > 0 && item.parentInventory != null && GeneralHelper.IsItemOwned(item) &&
            item.unitCount == preview.unitCount && JToken.DeepEquals(expectedTags, ManagerTags(item, keys)));
        R.Check("manager.spawn." + name, new { count, id = preview.identifier, unitCount = preview.unitCount, tags = expectedTags },
            new { status = Reflect.Str(panel, "status"), created = created.Select(item => ManagerReceipt(item, keys)).ToArray() }, valid,
            "Real ManagerPanel Spawn button -> ItemEditor.Capture -> ItemSpawner.Spawn -> DirectoryMaster.Item -> native SlotMarker.TryAcceptOnce; actual owned inventory UID delta");
        return created;
    }
    private static JObject ManagerTag(TagState tag) => JObject.FromObject(new { tag.valueEnabled, tag.valueInt,
        valueFloat = tag.valueFloat.ToString("R", CultureInfo.InvariantCulture), tag.valueLong,
        valueDouble = tag.valueDouble.ToString("R", CultureInfo.InvariantCulture), tag.valueString, tag.valueBool });
    private static JObject ManagerAllTags(GameItem item) => new(item.state.dict.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => new JProperty(pair.Key, pair.Value == null ? JValue.CreateNull() : ManagerTag(pair.Value))));
    private static JObject ManagerTags(GameItem item, IEnumerable<string> keys) => new(keys.OrderBy(key => key, StringComparer.Ordinal)
        .Select(key => new JProperty(key, item.state.dict.TryGetValue(key, out TagState tag) ? ManagerTag(tag) : new JObject { ["missing"] = true })));
    private static JObject ManagerReceipt(GameItem item, IEnumerable<string> keys) => JObject.FromObject(new { id = item.identifier, uid = item.uniqueId, count = item.unitCount,
        owned = GeneralHelper.IsItemOwned(item), parentStorage = item.parentInventory?.identifier, tags = ManagerTags(item, keys) });

    internal IEnumerable<object?> ItemManagerReload(JObject baseline)
    {
        R.phase = "manager-independent-process-es3-numeric-readback";
        string expectedLocale = O.Family == "manageren" ? "en" : "zh";
        string? actualLocale = ModHelper.GetCurrentLocale()?.Identifier.Code;
        R.environment["managerLocale"] = actualLocale;
        R.Check("manager.reload-process-locale", expectedLocale, actualLocale, actualLocale == expectedLocale,
            "Fresh-process main-menu selection remains active after native InventoryScene LoadGame; no store-scene locale mutation.");
        JObject state = (JObject)baseline["persistence"]!["manager"]!;
        var receipts = ((JArray)state["items"]!).Cast<JObject>().Concat(new[] { (JObject)state["existingControl"]! }).ToArray();
        foreach (JObject expected in receipts)
        {
            int uid = (int)expected["uid"]!;
            GameItem? item = Store.FindAllItem().SingleOrDefault(value => value.uniqueId == uid);
            string[] keys = ((JObject)expected["tags"]!).Properties().Select(property => property.Name).ToArray();
            JObject? observed = item == null ? null : ManagerReceipt(item, keys);
            R.Check("manager.reload-exact-native-tag-slots." + uid, expected, observed, JToken.DeepEquals(expected, observed),
                "Fresh process normal LoadGame/ES3, matched actual generated UID; enabled/int/float/double/long/string/bool slots compared independently of generic SnapshotItems");
            yield return null;
        }
        R.persistence["manager"] = new { verifiedUids = receipts.Select(item => (int)item["uid"]!).ToArray(), sourceBaseline = O.Baseline };
    }
}
