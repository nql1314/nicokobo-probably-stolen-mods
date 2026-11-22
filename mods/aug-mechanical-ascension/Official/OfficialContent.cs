using Nicokobo.ContentInterop;

namespace AugMechanicalAscension.Official;

internal enum NativeApplicationStatus { Staged,Applied,Failed }
internal sealed record ContentView(string OwnerId,string ContentId,NativeApplicationStatus Status);

internal static class OfficialItemApi
{
    private sealed record Definition(string Owner,string Id,Func<GameItem> Factory,NativeItemOptions? Text);
    private static readonly Dictionary<string,Definition> Definitions = new(StringComparer.Ordinal);
    private static readonly List<(string Id,Func<bool> MayOffer)> Nodes = new();
    private static readonly List<StockOffer> Supply=new();
    internal static bool Ready { get; private set; }
    internal static SubmitResult RegisterItem(string owner,string id,Func<GameItem> factory,NativeItemOptions? options=null)
    {
        if (Definitions.TryGetValue(id,out var previous)) return new(previous.Owner == owner ? SubmitStatus.AlreadyPresent : SubmitStatus.Conflict,"Item already declared");
        Definitions.Add(id,new(owner,id,() =>
        {
            var item=factory();
            options?.Apply(item);
            ModHelper.SetSource(item,MechcorePackage.Id,AugText.Get("package.name"));
            return item;
        },options));
        if(options?.NpcTrade?.Category==NpcTradeStockCategory.Medical)Supply.Add(new StockOffer(id,StockCategory.Medical));
        return new(SubmitStatus.Accepted,"Staged for official item directory");
    }
    internal static SubmitResult RegisterNode(string owner,string id,Func<GameItem> factory,NativeNodeOptions options)
    { var result=RegisterItem(owner,id,factory,new(options.Name,options.ShortDescription,options.FlavorText)); if (result.Status == SubmitStatus.Accepted) Nodes.Add((id,options.CanOfferAtNightShop)); return result; }
    internal static void LocalizeTooltip(RichTextBuilder builder,GameItem item)
    {
        if(Definitions.TryGetValue(item.identifier,out var definition))
            definition.Text?.Apply(item);
    }
    internal static void Apply(ModItemDirectory directory)
    {
        OfficialEffectApi.Apply();
        foreach(var definition in Definitions.Values)
            if (!directory.Add(definition.Id,definition.Factory)) throw new InvalidOperationException("Item registration failed: " + definition.Id);
        Ready=true;
        if(Supply.Count>0)SupplyCatalog.Register(AugMod.Id,Supply);
        foreach(var view in Snapshot())OfficialContentApi.Notify(view);
    }
    internal static GameItem Create(string id,bool owned=true) => DirectoryMaster.Item(id,owned);
    internal static bool Exists(string id) => DirectoryMaster.Has<GameItem>(id);
    internal static ContentView[] Snapshot() => Definitions.Values.Select(x => new ContentView(x.Owner,x.Id,Ready?NativeApplicationStatus.Applied:NativeApplicationStatus.Staged)).ToArray();
    internal static string[] NightShop(GameItem[] owned) => Nodes.Where(node => node.MayOffer()).Select(node => node.Id).ToArray();

}
internal static class OfficialEffectApi
{
    private static readonly Dictionary<string,(string Owner,Func<ModuleEffectHelper.ModuleEffect> Factory,Func<GameItem,bool> AppliesTo)> Definitions = new();
    internal static bool Ready { get; private set; }
    internal static SubmitResult RegisterFixedAbility(string owner,string id,Func<ModuleEffectHelper.ModuleEffect> factory,Func<GameItem,bool> appliesTo)
    { if (Definitions.ContainsKey(id)) return new(SubmitStatus.AlreadyPresent,"Already declared"); Definitions.Add(id,(owner,factory,appliesTo)); return new(SubmitStatus.Accepted,"Staged"); }
    internal static void Apply()
    {
        foreach(var entry in Definitions)
        {
            var ability=entry.Value.Factory();
            FixedModuleAbilities.Register(entry.Value.Owner,entry.Key,entry.Value.AppliesTo,
                () => ability.displayName,() => ability.description,ability.onAnyAction);
        }
        Ready=true;
        foreach(var view in Snapshot())OfficialContentApi.Notify(view);
    }
    internal static ContentView[] Snapshot() => Definitions.Select(x => new ContentView(x.Value.Owner,x.Key,Ready?NativeApplicationStatus.Applied:NativeApplicationStatus.Staged)).ToArray();
}
internal static class OfficialContentApi
{
    private static event Action<ContentView>? Changed;
    internal static IDisposable Subscribe(string owner,string id,Action<ContentView> callback)
    { Changed+=callback;return new CallbackLease(()=>Changed-=callback); }
    internal static void Notify(ContentView view)=>Changed?.Invoke(view);
}

