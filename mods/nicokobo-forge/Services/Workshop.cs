using Nicokobo.ModUtilities;
using HarmonyLib;
using Nicokobo.Forge;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace Nicokobo.ContentInterop;
internal static class OfficialWorkshopApi
{
    private sealed record Chain(string Owner, string Chinese, string English, Func<OfficialWorkshopSnapshot?> Snapshot,
        Action<string> Claim, Action? Opened, Func<string>? LocalizedTitle);
    private static readonly Dictionary<string,Chain> Chains=new();
    private static CustomUIWindow? _window;
    private static string? _current,_selected;
    private static string _lastView="";
    private static DateTime _refresh;
    private static bool _phoneRequested;
    private static ConfigEntry<bool>? _hotkeyEnabled;
    private const string CardId="nicokobo.forge.nico_card";
    private const string Website="nicokobo.com";
    private const long PhoneNumber=1016;
    internal static bool IsVisible=>_window?.IsVisible==true;
    internal static bool PointerOverWindow => IsVisible && _window!.Rect != null &&
        RectTransformUtility.RectangleContainsScreenPoint(_window.Rect, Input.mousePosition,
            _window.root.GetComponentInParent<Canvas>()?.worldCamera);
    internal static void DefineConfig()
    {
        _hotkeyEnabled = ModHelper.GetConfig(Nicokobo.Forge.ForgeMod.Id).Define("EnableAchievementHotkey", true, "forge.config.controls",
            () => ForgeText.Get("forge.enable_n_key_for_achievements"));
    }
    internal static SubmitResult RegisterChain(string owner,string id,string chinese,string english,Func<OfficialWorkshopSnapshot?> snapshot,Action<string> claim,Action? opened=null,Func<string>? localizedTitle=null)
    {
        if(Chains.ContainsKey(id))return new(SubmitStatus.AlreadyPresent,"Chain exists");
        ContentRuntime.RequireOwner(owner); Chains.Add(id,new(owner,chinese,english,snapshot,claim,opened,localizedTitle));return new(SubmitStatus.Accepted,"Native workshop registered");
    }
    internal static bool OpenChain(string id)=>OpenTab(id);
    internal static void SetDefaultChain(string id) { if (_current == null && Chains.ContainsKey(id)) _current = id; }
    internal static bool OpenDefault()
    {
        var id=_current??Chains.Keys.FirstOrDefault();
        if(id!=null)return OpenTab(id);
        UnityEngine.Debug.LogWarning("[Workshop] No workshop page is registered.");return false;
    }
    internal static bool OpenTab(string id)
    {
        try
        {
            if(!PlayerStore.HasInstance||EmporiumEntry.Instance?.invElement==null)
                throw new InvalidOperationException("The store scene is not ready.");
            if(CustomUIManager.Instance==null)throw new InvalidOperationException("The game UI is unavailable.");
            if(!Chains.TryGetValue(id,out var chain))throw new InvalidOperationException("Workshop page is not registered: "+id);
            // Opening a page validates its goals once. Polling/rendering only reads the resulting snapshot.
            chain.Opened?.Invoke();
            var snapshot=chain.Snapshot()??throw new InvalidOperationException("Workshop page is not ready: "+id);
            _current=id;
            if(!snapshot.Entries.Any(entry=>entry.Id==_selected))_selected=snapshot.Entries.FirstOrDefault()?.Id;
            Render(snapshot);
            if(!IsVisible)throw new InvalidOperationException("Workshop window did not become visible.");
            return true;
        }
        catch(Exception ex){UnityEngine.Debug.LogWarning("[Workshop] Could not open: "+ex);return false;}
    }
    internal static void CloseWindow()
    {
        var window = _window;
        _window = null; _lastView = ""; _phoneRequested = false;
        window?.Close();
        PresentationInputGuard.Closed();
    }
    internal static void RegisterPhone()
    {
        var result=OfficialPhoneApi.Register(Nicokobo.Forge.ForgeMod.Id,new(Nicokobo.Forge.ForgeMod.Id+".workshop.phone",PhoneNumber,
            _=>true,_=>ForgeText.Get("forge.nico_workshop"),_=>
            {
                // Finish native call setup before hanging up and opening the UI.
                _phoneRequested=true;
                return new OfficialDialogueScript(Nicokobo.Forge.ForgeMod.Id,new(Nicokobo.Forge.ForgeMod.Id+".workshop.call",
                    ForgeText.Get("forge.nico_workshop"),ForgeText.Get("forge.connected_opening_the_workshop"),
                    [],"OK","Back","Close","Later","Unavailable") { AllowLater=false });
            }));
        if(result.Status!=OfficialPresentationStatus.Queued)
            throw new InvalidOperationException("Workshop phone registration: "+result.Status+"; "+result.Reason);
    }
    internal static void Update()
    {
        // The scene owns availability; the persistent NewGameData menu flag can
        // outlive a menu transition and used to close a freshly opened card.
        if(!PlayerStore.HasInstance||EmporiumEntry.Instance?.invElement==null){CloseWindow();return;}
        if(_phoneRequested)
        {
            _phoneRequested=false;
            if(PhoneUIManager.Instance is {} phone)
            {
                phone.CloseUI();
                phone.OnPhoneDialogEnd(false);
            }
            OpenDefault();
        }
        if(_hotkeyEnabled?.Value==true&&Input.GetKeyDown(KeyCode.N)&&!KeyBindHelper.SuppressHotkeys&&!Nicokobo.Forge.Input.TextInputFocus.ShouldSuppressHotkeys())
        {if(IsVisible)CloseWindow();else OpenDefault();}
        if(!IsVisible||DateTime.UtcNow<_refresh||_current==null)return;
        _refresh=DateTime.UtcNow.AddMilliseconds(500);
        if(Chains[_current].Snapshot() is not {} snapshot){CloseWindow();return;}
        if(ViewKey(snapshot)!=_lastView)Render(snapshot);
    }
    private static string ViewKey(OfficialWorkshopSnapshot snapshot) =>
        ModText.LocaleCode + "|" + string.Join("|", snapshot.Entries.Select(e => $"{e.Id}:{e.StateText}:{e.ActionEnabled}:{e.CostText}")) + snapshot.Message;

    private static void Render(OfficialWorkshopSnapshot? view=null)
    {
        if(_current==null||!Chains.TryGetValue(_current,out var chain))return;
        var snapshot=view??chain.Snapshot();
        if(snapshot==null)return;
        bool wasVisible = IsVisible;
        Vector2? position=_window?.IsAlive==true?_window.Rect.anchoredPosition:null;
        // Replacing the rendered window must not release its active gesture.
        _window = null;
        var ui=CustomUIManager.Instance.CreateWindow("nicokobo.forge.workshop",ForgeText.Get("forge.nico_workshop"),"overlay",
            style: CustomUIManager.Style.Machinery).SetDraggable(true).SetCloseOnEscape();
        // Fit the graph while retaining the frame's header and content padding.
        var content = ui.Handle.ContentRoot;
        ui.SetSize(1040, WorkshopGraph.Height + content.offsetMin.y - content.offsetMax.y);
        ui.OnClose(() =>
        {
            if (_window != ui.Handle) return;
            _window = null; _lastView = "";
            PresentationInputGuard.Closed();
        });
        ui.Handle.root.GetComponent<UnityEngine.UI.Image>().color = WorkshopGraph.PanelTint;
        RenderHeader(ui.Handle, snapshot);
        ui.BeginRow(12);
        WorkshopGraph.Draw(ui, snapshot, _selected, id => { _selected=id; Render(); });
        ui.BeginPanel(336, WorkshopGraph.Height, UnityEngine.TextAnchor.UpperLeft, 12, 8);
        ui.Last.SetImageColor(WorkshopGraph.PanelTint);
        var selected=snapshot.Entries.FirstOrDefault(e=>e.Id==_selected)??snapshot.Entries.FirstOrDefault();
        if (selected != null) RenderDetails(ui, snapshot, selected, chain);
        ui.End();ui.End();
        if(position.HasValue)ui.SetPosition(position.Value);else ui.Center();
        _window=ui.Show();
        if (!wasVisible) PresentationInputGuard.Opened();
        // A live achievement refresh recreates this sibling; keep its query above it.
        RecipeQuery.BringToFront();
        _lastView=ViewKey(snapshot);
    }
    private static void RenderHeader(CustomUIWindow window, OfficialWorkshopSnapshot snapshot)
    {
        var title = window.frame.titleText;
        title.color = WorkshopGraph.Accent;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.fontSize = 18;
        title.rectTransform.anchorMin = Vector2.zero;
        title.rectTransform.anchorMax = new Vector2(0, 1);
        title.rectTransform.offsetMin = new Vector2(48, 0);
        title.rectTransform.offsetMax = new Vector2(208, 0);
        var header = (RectTransform)title.transform.parent;
        header.GetComponent<Image>().color = WorkshopGraph.PanelTint;
        var brand = new CustomUIBuilder(CustomUIManager.Instance, window, header);
        brand.AddImage(Nicokobo.Forge.ForgeMod.Id + ":nicokobo_logo", 24, 24);
        var logo = brand.Last.Rect;
        logo.anchorMin = logo.anchorMax = new Vector2(0, .5f);
        logo.pivot = new Vector2(.5f, .5f);
        logo.sizeDelta = new Vector2(24, 24);
        logo.anchoredPosition = new Vector2(28, 0);
        brand.AddLabel(ForgeText.Get("forge.website") + Website);
        var website = brand.Last;
        website.Rect.anchorMin = website.Rect.anchorMax = new Vector2(1, .5f);
        website.Rect.pivot = new Vector2(1, .5f);
        website.Rect.sizeDelta = new Vector2(200, 28);
        website.Rect.anchoredPosition = new Vector2(-48, 0);
        website.SetTextColor(WorkshopGraph.Accent);
        website.text.fontSize = 14;
        website.text.alignment = TextAlignmentOptions.MidlineRight;
        website.text.enableWordWrapping = false;
        var tabs = new CustomUIBuilder(CustomUIManager.Instance, window, header);
        tabs.BeginRow(6, TextAnchor.MiddleLeft);
        var tabBar = tabs.CurrentContainer;
        tabBar.anchorMin = Vector2.zero;
        tabBar.anchorMax = Vector2.one;
        tabBar.offsetMin = new Vector2(216, 0);
        tabBar.offsetMax = new Vector2(-264, 0);
        foreach (var page in Chains.ToArray())
        {
            string id = page.Key;
            bool active = id == _current;
            string name = page.Value.LocalizedTitle?.Invoke() ?? (snapshot.English ? page.Value.English : page.Value.Chinese);
            tabs.AddButton(name, () => OpenTab(id));
            var tab = tabs.Last;
            tab.image.sprite = MachineryStyle.TabPlate;
            tab.image.color = Color.white;
            tab.button.transition = Selectable.Transition.ColorTint;
            var colors = tab.button.colors;
            colors.normalColor = active ? WorkshopGraph.PanelTint : MachineryStyle.Hex("10212a");
            colors.highlightedColor = MachineryStyle.Hex("2b4b57");
            colors.pressedColor = MachineryStyle.Hex("3a6670");
            colors.selectedColor = colors.disabledColor = colors.normalColor;
            tab.button.colors = colors;
            tab.SetInteractable(!active);
            tab.text.fontSize = 16;
            tab.text.alignment = TextAlignmentOptions.MidlineLeft;
            tab.text.enableWordWrapping = false;
            tab.text.overflowMode = TextOverflowModes.Ellipsis;
            tab.SetTextColor(active ? WorkshopGraph.Accent : MachineryStyle.TAB_TEXT);
            tab.SetFixedWidth(Mathf.Clamp(tab.text.preferredWidth + 62, 112, 320)).SetPreferredSize(-1, 36);
            tab.root.GetComponent<HorizontalLayoutGroup>().spacing = 8;
            // Read presentation snapshots only; validation remains in OpenTab.
            var goal = (active ? snapshot : page.Value.Snapshot())?.Entries
                .FirstOrDefault(entry => entry.GraphX == 0 && entry.GraphY == 0);
            var icon = id == NativeWorkshop.ChainId ? NativeWorkshop.ReadIcon(AchievementRules.VictoryNode)
                : goal == null ? null : WorkshopGraph.ReadIcon(goal);
            var contents = new CustomUIBuilder(CustomUIManager.Instance, window, tab.Rect);
            if (icon != null)
            {
                contents.AddImage(icon, 26, 26);
                contents.Last.SetFixedWidth(26);
                contents.Last.root.transform.SetAsFirstSibling();
            }
            if (active)
            {
                contents.AddImage(MachineryStyle.White);
                var underline = contents.Last;
                underline.root.GetComponent<LayoutElement>().ignoreLayout = true;
                underline.Rect.anchorMin = Vector2.zero;
                underline.Rect.anchorMax = new Vector2(1, 0);
                underline.Rect.offsetMin = new Vector2(8, 0);
                underline.Rect.offsetMax = new Vector2(-8, 2);
                underline.image.preserveAspect = false;
                underline.SetImageColor(WorkshopGraph.Accent);
            }
        }
        tabs.End();
    }
    private static void RenderDetails(CustomUIBuilder ui, OfficialWorkshopSnapshot snapshot,
        OfficialWorkshopEntry selected, Chain chain)
    {
        ui.BeginScroll(500);
        DetailLabel(ui, selected.Title, 22);
        ui.Last.SetTextColor(WorkshopGraph.Accent);
        DetailLabel(ui, selected.Subtitle, 14);
        ui.AddSeparator();
        DetailLabel(ui, selected.Description);
        ui.AddSpace(8);
        DetailLabel(ui, selected.Requirement);
        DetailLabel(ui, selected.CostText);
        var proof=selected.Conditions.FirstOrDefault(condition=>condition.Kind==OfficialWorkshopResourceKind.Item);
        if(proof!=null&&DirectoryMaster.Has<GameItem>(proof.ResourceId)&&RecipeQuery.HasRecipesForItem(proof.ResourceId))
            ui.AddButton(ForgeText.Get("forge.find_crafting_recipes"),()=>Nicokobo.ContentInterop.RecipeQuery.OpenItem(proof.ResourceId));
        if(selected.Dependencies.Count>0)DetailLabel(ui,(ForgeText.Get("forge.requires"))+string.Join(" → ",selected.Dependencies.Select(id=>snapshot.Entries.FirstOrDefault(e=>e.Id==id)?.Title??id)));
        ui.AddSpace(8);
        if (selected.Rewards.Count > 0)
            DetailLabel(ui, ModText.Get(ForgeMod.Id, "workshop.reward", string.Join(" / ", selected.Rewards.Select(reward =>
            {
                string label = reward.DisplayName ?? reward.ResourceId;
                if (reward.Kind == OfficialWorkshopResourceKind.Item && reward.ResourceId.IndexOf('.') < 0)
                    label = Nicokobo.ModUtilities.NativeItemText.Name(reward.ResourceId, label);
                return $"{label} ×{reward.Quantity}";
            }))));
        DetailLabel(ui,selected.StateText);
        ui.Last.SetTextColor(WorkshopGraph.StateColor(selected));
        if(!string.IsNullOrWhiteSpace(snapshot.Message))DetailLabel(ui,snapshot.Message);
        ui.End();
        ui.AddButton(selected.ActionText,()=>{chain.Claim(selected.Id);Render();});
        ui.Last.SetFixedWidth(312).SetInteractable(selected.ActionEnabled);
    }
    private static void DetailLabel(CustomUIBuilder ui, string text, float size = 16)
    {
        ui.AddLabel(text);
        ui.Last.SetFixedWidth(284);
        ui.Last.text.fontSize = size;
        ui.Last.text.enableWordWrapping = true;
    }
    internal static void RegisterCard(ModItemDirectory directory)
    {
        if (!directory.Add(CardId, () =>
        {
            var item = DirectoryMaster.Create<GameItem>("joe_card"); item.identifier = CardId;
            item.SetSpriteAndShapeFromMod(Nicokobo.Forge.ForgeMod.Id, "nico_card");
            item.SetShape(new GridShapeBuilder().SetData("11", 2).Build());
            item.EnableTag("NOT_FOR_RESALE", false);
            item.name = ForgeText.Get("forge.nico_workshop_card");
            RefreshCardText(item);
            // Saved description strings overwrite the factory values during loading.
            item.onLoaded += RefreshCardText;
            // The vanilla card template also carries activation callbacks.
            item.activateDefault = false; item.forceDisableActivate = false; item.forceDisableUse = false;
            item.mayActivateSlotItemFunc = null; item.canActivateSlotItemFunc = null;
            item.onActivateSlotItemFunc = null;
            ModHelper.SetSource(item, Nicokobo.Forge.ForgeMod.Id, "NicoKobo Forge");
            return item;
        })) throw new InvalidOperationException("Workshop card ID collision");
    }
    private static void RefreshCardText(GameItem item)
    {
        item.shortDescription = ForgeText.Get("forge.website_press_n_or_call_to_open", Website, PhoneNumber);
        item.flavorText = ForgeText.Get("forge.adds_special_achievement_paths_to_the_game");
    }
    internal static void GrantOpeningCard()
    {
        if (!PlayerStore.HasInstance || PlayerStore.Instance.FindAllItem().Any(item => item.identifier == CardId)) return;
        var card = DirectoryMaster.Item(CardId);
        var inventory = EmporiumEntry.Instance.invElement;
        var plan = InventoryPlacement.PlanWholeGrid(inventory, new[] { card });
        if (plan == null) { card.Destroy(); throw new InvalidOperationException("No room for workshop card"); }
        card.modifiedShape = plan[0].Shape;
        if (GraphUtils.TryAcceptOnce(inventory, card) != 0 || card.parentInventory != inventory)
        { if (card.parentInventory == null) card.Destroy(); throw new InvalidOperationException("Workshop card placement failed"); }
    }
    internal static void Remove(string owner)
    {
        foreach (var id in Chains.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray())
        { Chains.Remove(id); if (_current == id) { CloseWindow(); _current = _selected = null; } }
    }
    internal static void Clear() { CloseWindow(); Chains.Clear(); _current = _selected = null; }
}
