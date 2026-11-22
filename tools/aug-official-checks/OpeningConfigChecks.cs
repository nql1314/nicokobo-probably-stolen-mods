extern alias aug;
using System.Collections;
using System.Reflection;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

internal static class OpeningConfigChecks
{
    internal static void Run(Action<bool,string> check)
    {
        // Read the actual shipped opening specs, not a mirror of its algorithm.
        var type=typeof(aug::AugMechanicalAscension.AugMod).Assembly.GetType("AugMechanicalAscension.Integration.OpeningRuntimeFlow",true)!;
        var specs=((IEnumerable)type.GetField("Specs",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>();
        var items=specs.Select(spec=>new OpeningItemSnapshot(
            (string)spec.GetType().GetProperty("Kind")!.GetValue(spec)!,
            (string)spec.GetType().GetProperty("ItemId")!.GetValue(spec)!,
            (int)spec.GetType().GetProperty("Units")!.GetValue(spec)!,0,true,true)).ToArray();
        var results=new List<(bool Passed,string Name)>();
        void Assert(bool ok,string name)=>results.Add((ok,name));
        const string run="10000000-0000-0000-0000-000000000001";
        OpeningSavePlanStatus Plan(ConfigSnapshot config)
        {
            var pending=RunStateCodec.CreateNew("steam-release-26317870",run,7,1,config);
            return OpeningSavePlanner.Prepare(pending,new(run,7,1,true,true,items),true,RunStateCodec.Write(pending)).Status;
        }
        Assert(items.Length==7&&items.Where(item=>item.Kind is "battery" or "medical" or "electronic").All(item=>item.Units==1),
            "Actual shipped opening specs use the fixed one-unit supplies");
        foreach(string field in new[]{"initialBatteryCount","initialMedicalCount","initialElectronicCount"})
        {
            var parsed=ConfigSnapshotLoader.Parse("{\""+field+"\":2}");
            var plan=Plan(parsed.Config);
            Console.WriteLine($"CONFIG {field}=2; parse={parsed.Status}; opening={plan}; reason={parsed.Error??"none"}");
            Assert(parsed.Status==ConfigLoadStatus.Invalid&&parsed.Error?.Contains(field,StringComparison.Ordinal)==true&&
                parsed.Error.Contains("fixed",StringComparison.OrdinalIgnoreCase),field+" rejected at config entry with its fixed-count reason");
            Assert(plan==OpeningSavePlanStatus.Ready,field+" invalid config returns safe defaults accepted by the real opening planner");
        }
        foreach(string json in new[]{"{}","{\"initialBatteryCount\":1,\"initialMedicalCount\":1,\"initialElectronicCount\":1}",
            "{\"initialCredits\":10000}","{\"initialCredits\":650,\"initialRent\":275,\"dailyHealthLoss\":8}"})
        {
            var parsed=ConfigSnapshotLoader.Parse(json);
            Assert(parsed.Status==ConfigLoadStatus.Loaded&&Plan(parsed.Config)==OpeningSavePlanStatus.Ready,
                "Supported config reaches the actual opening planner: "+json);
        }
        foreach(int count in new[]{2,3})
        foreach(var config in new[]{new ConfigSnapshot{InitialBatteryCount=count},new ConfigSnapshot{InitialMedicalCount=count},new ConfigSnapshot{InitialElectronicCount=count}})
        {
            var history=RunStateCodec.CreateNew("steam-release-26317870",run,7,1,config);
            string bytes=RunStateCodec.Write(history);
            var restored=RunStateCodec.ReadForGame(bytes,run,7);
            Assert(restored.Status==ReadStatus.Ready&&Plan(restored.State!.ConfigSnapshot)==OpeningSavePlanStatus.InvalidManifest&&
                RunStateCodec.Write(restored.State)==bytes,
                "Existing saved pending count remains readable and InvalidManifest without automatic rewrite: "+
                config.InitialBatteryCount+"/"+config.InitialMedicalCount+"/"+config.InitialElectronicCount);
        }
        foreach(var result in results)Console.WriteLine((result.Passed?"PASS ":"FAIL ")+result.Name);
        foreach(var result in results)check(result.Passed,result.Name);
    }
}
