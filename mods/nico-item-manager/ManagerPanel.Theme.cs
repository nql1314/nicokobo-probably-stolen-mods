using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NicoItemManager
{
    internal sealed partial class ManagerPanel
    {
        static void AddTextInput(CustomUIBuilder builder, string placeholder, string initial,
            Action<string> onChanged = null, string id = null, Action<string> onTyping = null)
        {
            // Faucet Water Mixer overrides the shared Machinery input factory with a numeric field.
            // Build the stock text field first, then apply the Machinery skin to this instance.
            bool machinery = builder.Handle.useMachineryStyle;
            builder.UseMachineryStyle(false);
            try
            {
                builder.AddInput(placeholder, initial, onChanged, id, onTyping);
            }
            finally
            {
                builder.UseMachineryStyle(machinery);
            }
            if (machinery) CustomUIMachineryStyle.Reskin(builder.Last.root);
        }

        // Style this window's controls only; the native Machinery theme supplies fonts and sprites.
        static void StyleControls(RectTransform root)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                var text = button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (text == null) continue;
                text.fontSize = 18;
                text.richText = false;
                text.enableAutoSizing = false;
            }
            foreach (TMP_InputField input in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (input.targetGraphic is Image plate)
                {
                    plate.sprite = MachineryStyle.PlateNotched;
                    plate.color = MachineryStyle.LABEL_PLATE;
                }
                input.textComponent.color = MachineryStyle.LABEL_TEXT;
                input.textComponent.fontSize = 18;
                input.customCaretColor = true;
                input.caretColor = MachineryStyle.LABEL_TEXT;
                input.selectionColor = new Color(0.47f, 0.45f, 0.4f, 0.4f);
                if (input.placeholder != null)
                {
                    Color color = MachineryStyle.LABEL_TEXT;
                    color.a = 0.6f;
                    input.placeholder.color = color;
                }
            }
            foreach (TMP_Dropdown dropdown in root.GetComponentsInChildren<TMP_Dropdown>(true))
            {
                if (dropdown.targetGraphic is Image plate)
                {
                    plate.sprite = MachineryStyle.PlateNotched;
                    plate.color = MachineryStyle.LABEL_PLATE;
                }
                dropdown.captionText.color = MachineryStyle.LABEL_TEXT;
                dropdown.captionText.fontSize = 18;
                dropdown.itemText.color = MachineryStyle.BODY_TEXT;
                dropdown.itemText.fontSize = 18;
                // Native fallback reskin also replaces the arrow and checkmark with panel sprites.
                LoadedMod assets = ModLoader.Mods.FirstOrDefault(mod => mod.Manifest.ID == NicoItemManagerMod.Id);
                Sprite arrow = null;
                assets?.PaperSprites.TryGetValue("control_down", out arrow);
                foreach (Image image in dropdown.GetComponentsInChildren<Image>(true))
                {
                    if (!string.Equals(image.name, "Arrow", StringComparison.OrdinalIgnoreCase)) continue;
                    image.sprite = arrow;
                    image.type = Image.Type.Simple;
                    image.preserveAspect = true;
                    image.color = Color.white;
                }
                Toggle option = dropdown.itemText.GetComponentInParent<Toggle>();
                if (option != null && option.graphic is Image checkmark)
                {
                    checkmark.sprite = MachineryStyle.RadioOn;
                    checkmark.type = Image.Type.Simple;
                    checkmark.preserveAspect = true;
                    checkmark.color = Color.white;
                }
            }
        }
    }
}
