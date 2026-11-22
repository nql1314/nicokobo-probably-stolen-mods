using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Official;
using UnityEngine;
namespace AugMechanicalAscension.Integration;
internal sealed partial class StoryRuntimeFlow
{
    private GameItem? _magnifiedItem;
    private InspectionUIManager? _magnifierUi;
    private CustomUIWindow? _magnifierWindow;
    private long? _magnifierGeneration;
    private bool CanMagnify(GameItem item) => Current() && item.unitCount > 0 &&
        (item.identifier == StoryEvidence.Prefix + "broken" || item.identifier == StoryEvidence.Prefix + "photo") &&
        OfficialInventoryApi.IsPlayerOwned(item);
    private void ShowMagnified(InspectionUIManager ui, GameItem item)
    {
        try
        {
            CloseMagnifier();
            if (!CanMagnify(item)) { ui.CloseUI(); return; }
            string key = item.identifier[StoryEvidence.Prefix.Length..];
            _magnifiedItem = item; _magnifierUi = ui; _magnifierGeneration = _generation;
            var builder = OfficialStoryUi.Create("nicokobo.aug.story.inspection", item.name, 660, 490, () => CloseMagnifierCore(false));
            _magnifierWindow = builder.Handle;
            var sprite = StoryIconAtlas.MagnifierSprite(key) ?? throw new InvalidOperationException("Evidence inspection image is unavailable");
            float scale = Math.Min(600f / sprite.rect.width, 260f / sprite.rect.height);
            builder.AddImage(sprite, sprite.rect.width * scale, sprite.rect.height * scale, "evidence").BeginScrollFlexible(110);
            OfficialStoryUi.Body(builder, StoryEvidence.MagnifierNote(key, null), "note");
            builder.End().AddButton(AugText.Get("aug.close"), CloseMagnifier, "close");
            OfficialStoryUi.Show(builder);
            var previous = _actionItem; _actionItem = item;
            try { if (Ready && ReadableState(item) != null) InspectEvidence(key); }
            finally { _actionItem = previous; }
        }
        catch (Exception error) { log("[ERROR] [Story] Magnifier: " + error.Message); CloseMagnifier(); ui.CloseUI(); }
    }
    private bool MagnifierCurrent() => _magnifiedItem != null && _magnifierUi != null && _magnifierWindow?.IsVisible == true &&
        _magnifierUi.panel != null && _magnifierUi.panel.activeInHierarchy &&
        ReferenceEquals(_magnifierUi.currentInspectingItem, _magnifiedItem) && _magnifierGeneration == _generation && CanMagnify(_magnifiedItem);
    private void CloseMagnifier() => CloseMagnifierCore(true);
    private void CloseMagnifierCore(bool closeWindow)
    {
        var window = _magnifierWindow; _magnifierWindow = null;
        var ui = _magnifierUi; var item = _magnifiedItem;
        _magnifiedItem = null; _magnifierUi = null; _magnifierGeneration = null;
        if (closeWindow) window?.Close();
        if (ui != null && item != null && ReferenceEquals(ui.currentInspectingItem, item)) ui.CloseUI();
    }
    private bool UpdateMagnifierInput()
    {
        if (_magnifiedItem == null) return false;
        if (!MagnifierCurrent()) { CloseMagnifier(); return false; }
        if (OfficialStoryUi.PointerOverWindow) PsModInput.TextInputFocus.BlockCurrentFrame();
        return true;
    }
    private bool DrawMagnifier() => MagnifierCurrent();
}
