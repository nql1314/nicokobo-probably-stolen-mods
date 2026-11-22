extern alias aug;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class ProtectedItemChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var type=typeof(aug::AugMechanicalAscension.AugMod).Assembly.GetType("AugMechanicalAscension.Integration.ProtectedCyberneticItems",true)!;
        const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;
        var installed=type.GetField("_installed",hidden)!;object? previous=installed.GetValue(null);
        var results=new List<(bool Passed,string Name)>();int nextUid=100;
        void Assert(bool ok,string name)=>results.Add((ok,name));
        object? Call(string method,params object?[] args)=>type.GetMethod(method,hidden)!.Invoke(null,args);
        GameItem Item(string id)
        {
            var item=(GameItem)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
            item.identifier=id;item.unitCount=1;item.uniqueId=++nextUid;return item;
        }
        GameItem Bag(string id,params GameItem[] items)
        {
            var bag=Item(id);
            var grid=(GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory));
            typeof(GameGridInventory).GetField("items",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(grid,new List<GameItem>(items));
            var window=(PixelWindow)RuntimeHelpers.GetUninitializedObject(typeof(PixelWindow));
            typeof(PixelWindow).GetProperty("childElement")!.SetValue(window,grid);
            typeof(PixelWindow).GetProperty("parentItems")!.SetValue(window,new List<GameItem>{bag});
            typeof(GameItem).GetProperty("contentWindow")!.SetValue(bag,window);
            typeof(GameInventory).GetProperty("parent")!.SetValue(grid,window);
            foreach(var item in items)item.parentInventory=grid;
            return bag;
        }
        try
        {
            installed.SetValue(null,true); // Test-process guard installation only; no Harmony/native hooks.
            var empty=Bag("backpack_small");var normal=Item("common_electronic");
            Assert((bool)Call("SellPrefix",empty)!&&(bool)Call("SellPrefix",normal)!,"Empty actual backpack and ordinary item remain eligible for native sales");
            var ordinaryBag=Bag("backpack_large",Bag("backpack_small",normal));
            Assert((bool)Call("SellPrefix",ordinaryBag)!,"Nested ordinary-only actual containers remain eligible for sale");
            foreach(string id in new[]{"nicokobo.aug.story.evidence.receipt","nicokobo.aug.story.distress_core","nicokobo.aug.brain_computer_interface_module"})
            {
                var protectedItem=Item(id);var single=Bag("backpack_small",protectedItem);var nested=Bag("backpack_large",single);
                foreach(var candidate in new[]{protectedItem,single,nested})
                {
                    int uid=candidate.uniqueId,childUid=protectedItem.uniqueId;
                    var args=new object?[]{candidate,true};type.GetMethod("CanSellPostfix",hidden)!.Invoke(null,args);
                    Assert(!(bool)args[1]!,"Actual CanSell guard rejects root/one/two-level protected item: "+id+"/"+candidate.identifier);
                    Assert(!(bool)Call("SellPrefix",candidate)!,"Actual SellItem prefix rejects root/one/two-level protected item: "+id+"/"+candidate.identifier);
                    var batch=new List<GameItem>{normal,candidate,empty};Call("SellMultiplePrefix",batch);
                    Assert(batch.SequenceEqual(new[]{normal,empty}),"Actual batch filter preserves allowed items and removes protected subtree: "+id+"/"+candidate.identifier);
                    Assert(candidate.uniqueId==uid&&protectedItem.uniqueId==childUid&&protectedItem.unitCount==1,"Guard does not mutate real graph item identity/units: "+id+"/"+candidate.identifier);
                }
                Assert((bool)Call("ContainsStory",nested,true)!,"ContainsStory reaches native PixelWindow/inventory layers: "+id);
                if(id!="nicokobo.aug.brain_computer_interface_module")
                    Assert(!(bool)Call("TheftPrefix",nested)!,"Existing theft protection still rejects nested story subtree: "+id);
            }
            var negative=new object?[]{empty,false};type.GetMethod("CanSellPostfix",hidden)!.Invoke(null,negative);
            Assert(!(bool)negative[1]!,"Guard preserves a native rejection for an unprotected item");
            Assert((bool)Call("SellPrefix",new object?[]{null})!,"Null item does not create a new protection side effect");
        }
        finally{installed.SetValue(null,previous);}
        foreach(var result in results)Console.WriteLine((result.Passed?"PASS ":"FAIL ")+result.Name);
        foreach(var result in results)check(result.Passed,result.Name);
    }
}
