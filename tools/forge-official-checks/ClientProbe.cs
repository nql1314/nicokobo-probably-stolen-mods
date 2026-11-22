using Nicokobo.ContentInterop;

namespace ForgeChecks;

public static class ClientProbe
{
    public static int Starts, AssetLoads, Before, After, Consumed, Water, Flushes, FixedCalls, Prepares;
    public static bool Committed, ThrowAssets;
    public static int Stops;
    public static bool Connected => ForgeClient.Connected;
    public static object? LastView;
    public static string Output = "first";
    public static string AbilityName = "Fixed ability";
    private static string _owner = "";
    private static bool _active;
    public static void Enable(string owner, bool fixedAbilities = false)
    {
        _owner = owner;
        ForgeClient.Enable(owner, () => { if (fixedAbilities) FixedModuleAbilities.RequireVersion(1); Starts++; _active = true; ContentRuntime.Attach(owner); }, StopContent, Assets);
    }
    private static void Assets(LoadedMod mod)
    {
        if (mod.Manifest.ID != _owner) return;
        AssetLoads++;
        if (ThrowAssets) throw new InvalidOperationException("Missing required sprite");
    }
    private static void StopContent() { if (_active) { Stops++; ContentRuntime.Detach(_owner); } _active = false; }
    public static void Disable() => ForgeClient.Disable(StopContent);
    public static void RegisterRecipe() => ExtraRecipes.Register(_owner, new("recipe", "machine",
        new[] { new IngredientView("input", 2, item => item.identifier == "input", "原料", "Input") },
        "first", "产物", "Output", () => true, () => Output, 2,
        item => item.identifier == "machine", item => "Preview: " + item.identifier)
        { Priority = 7, PrepareOutput = (_, product, _, _) => { Prepares++; product.ModifyTag("prepared", tag => tag.SetInt(1)); } });
    public static bool PrepareRecipe()
    {
        var recipe = ExtraRecipes.ForMachine("machine").Single(); var product = new GameItem();
        recipe.PrepareOutput?.Invoke(new GameItem(), product, Array.Empty<GameItem>(), Array.Empty<int>());
        return recipe.Priority == 7 && product.GetTagReadonly("prepared").GetInt() == 1;
    }
    public static (string Id, string Output, bool Matches) Recipe()
    {
        var recipe = ExtraRecipes.ForMachine("machine").Single(); LastView = recipe;
        return (recipe.Id, recipe.ResolveOutputId(), recipe.Ingredients.Single().Matches(new() { identifier = "input" }));
    }
    public static int RecipeCount() => ExtraRecipes.ForMachine("machine").Count;
    public static (bool Allowed, string? Preview) RecipePreview(GameItem machine)
    {
        var recipe = ExtraRecipes.ForMachine("machine").Single();
        return (recipe.IsAvailableFor(machine), recipe.Preview?.Invoke(machine));
    }
    public static void OpenRecipes(string itemId) => RecipeQuery.OpenItem(itemId);
    public static void RegisterMachine(GameInventory input, GameInventory output) => MachineCatalog.Register(_owner, "machine",
        _ => new(input, output), () => Array.Empty<RecipeView>());
    public static GameInventory? Input(GameItem machine) => MachineCatalog.Ports(machine)?.Input;
    public static void Observe() => ProductionHooks.Subscribe(_owner, "observer", _ => Before++, result =>
    { After++; Committed = result.Committed; Consumed = result.Consumed.Sum(x => x.Amount); Water = result.WaterPartsConsumed; });
    public static (IDisposable Scope, Action Commit, Func<bool> Consumed) Begin(GameItem machine, GameInventory input, GameInventory output, GameSlotInventory? water = null)
    {
        var attempt = ProductionHooks.Begin(new(machine, new(input, output, water: water), null));
        return (attempt, attempt.Commit, () => attempt.HasInputConsumption);
    }
    public static void Power(GameItem battery) => DevicePower.Register(_owner, source => source == battery ? battery.Energy : null,
        (source, amount) => { if (source != battery) return null; battery.Energy -= amount; return true; });
    public static (bool Handled, bool Success) Debit(GameItem battery, int amount)
    { bool handled = DevicePower.TryDebit(battery, amount, out bool success); return (handled, success); }
    public static void Supply() => SupplyCatalog.Register(_owner, new[] { new StockOffer("stock", StockCategory.Food) { MinimumDay = 1, Suppliers = StockSupplier.Thief } });
    public static string[] Stock() => SupplyCatalog.Available(StockCategory.Food, StockSupplier.Thief).Select(x => x.Owner + ":" + x.ItemId).ToArray();
    public static void Flush() => ProductionHooks.RegisterNightFlush(_owner, () => Flushes++);
    public static void FixedAbility() => FixedModuleAbilities.Register(_owner, "fixed.ability", item => item.identifier == "fixed.node",
        () => AbilityName, () => "Fixed description", (item, _) =>
        { FixedCalls++; item.ModifyTag("bonus", tag => tag.SetInt(tag.GetInt() + 25)); });
    public static void AddAbility(GameItem item) => FixedModuleAbilities.Add(item, "fixed.ability", 25);
    public static bool HasAbility() => FixedModuleAbilities.Contains("fixed.ability");
    public static void RemoveAbilities() => FixedModuleAbilities.Remove(_owner);

    // The new views must retain callbacks when producer and consumer compile
    // their own contract types. Native dialogue rendering has separate ports.
    public static int ConversationCalls, Purchases;
    public static object[] DialoguePayload() => ForgeWire.Script(new OfficialDialogueScript("checks.presenter", new("checks.presenter.dialogue", "Speaker", "Body",
        new[] { new OfficialDialogueChoice("accept", "Accept", "Preview", context => { ConversationCalls++; return context.RunId + ":" + context.ChoiceId; }) },
        "Confirm", "Back", "Leave", "Later", "Failed") { RequireConfirmation = false, AllowLater = false }));
    public static string ConfirmPayload(object[] payload)
    {
        var script = ForgeWire.Script(payload);
        if (script.Definition.RequireConfirmation || script.Definition.AllowLater) throw new Exception("Dialogue flags were lost");
        return script.Definition.Choices.Single().Confirm(new(2, "run", 3, "checks.presenter", "request", script.Definition.Id, "accept"));
    }
    public static object[] SupplierPayload() => ForgeWire.Supplier(new OfficialSupplierDefinition("checks.presenter.supplier", "Vendor",
        _ => new("Menu", new[] { new OfficialSupplierOffer("buy", "Buy", "Cost", _ => { Purchases++; return "paid"; }) { Enabled = false } })
            { AutomaticOfferId = null, AutomaticDelaySeconds = 1.25 }, _ => { }, "Confirm", "Back", "Continue", "Leave", "Failed") { RequireConfirmation = false, AllowMenuRefresh = true });
    public static bool ReadSupplierPayload(object[] payload)
    {
        var supplier = ForgeWire.Supplier(payload); var context = new OfficialDialogueContext(2, "run", 3, "checks.presenter", "request", supplier.Id, "buy");
        var page = supplier.Capture(context);
        return !supplier.RequireConfirmation && supplier.AllowMenuRefresh && !page.Offers.Single().Enabled &&
            page.AutomaticDelaySeconds == 1.25 && page.Offers.Single().Purchase(context) == "paid";
    }
    public static object[] WorkshopPayload() => ForgeWire.Workshop(new OfficialWorkshopSnapshot(42, true, new[] { new OfficialWorkshopGroup("组", "Group", 1) },
        new[] { new OfficialWorkshopEntry("entry", "Title", "Sub", "Description", "Requirement", "Cost", "Node", "Ready", "Claim", false, true, true)
        { Dependencies = new[] { "before" }, Conditions = new[] { new OfficialWorkshopRequirement(OfficialWorkshopResourceKind.Item, "proof", 2, false, true) { DisplayName = "Proof" } },
          Rewards = new[] { new OfficialWorkshopReward(OfficialWorkshopResourceKind.Credits, "credits", 7) }, Completed = true,
          VisualLinks = new[] { "related" }, GraphX = .5f, GraphY = -.5f } }, "Message"));
    public static bool ReadWorkshopPayload(object[] payload)
    {
        var view = ForgeWire.Workshop(payload); var entry = view.Entries.Single();
        return view.Credits == 42 && view.Message == "Message" && entry.Conditions.Single().DisplayName == "Proof" &&
            !entry.Conditions.Single().Consume && entry.Rewards.Single().Quantity == 7 && entry.Completed == true &&
            entry.Dependencies.Single() == "before" && entry.VisualLinks.Single() == "related" && entry.GraphX == .5f && entry.GraphY == -.5f;
    }
    public static object[] PhonePayload() => ForgeWire.Phone(new OfficialPhoneContact("checks.presenter.phone", 1234, context => context.SlotId == 3,
        _ => "Caller", _ => ForgeWire.Script(DialoguePayload())));
    public static string CallPhonePayload(object[] payload)
    {
        var phone = ForgeWire.Phone(payload); var context = new OfficialPhoneContext(2, "run", 3);
        if (!phone.Visible(context) || phone.DisplayName(context) != "Caller") throw new Exception("Phone context was lost");
        return ConfirmPayload(ForgeWire.Script(phone.Call(context)));
    }
}
