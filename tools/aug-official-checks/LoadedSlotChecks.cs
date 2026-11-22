using System.Text.Json.Nodes;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using AugMechanicalAscension.Official;

internal static class LoadedSlotChecks
{
    // Production loaded-modData boundary. No ES3 carrier is reconstructed.
    internal static void Run(RunState ready, Action<bool, string> check)
    {
        string run = ready.GameRunId; int slot = ready.SaveSlotId;
        var results = new List<(bool Passed, string Name)>();
        void Assert(bool ok, string name) => results.Add((ok, name));
        void Case(string name, Action action)
        { try { action(); } catch (Exception ex) { Assert(false, name + ": unexpected " + ex); } }
        bool Same(IReadOnlyDictionary<string,string> a, IReadOnlyDictionary<string,string> b) =>
            a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var value) && value == x.Value);
        var story = new StoryState { GameRunId=run, SaveSlotId=slot, Choices=[new("E01","investigate",2)],
            LastStartedDay=2, Items=[new("receipt",601),new("broken",602)], CatchUpOpening=false };
        var supply = GraylineSupplyRules.Schedule(new(){GameRunId=run,SaveSlotId=slot,Unlocked=true},1,
            Guid.NewGuid().ToString("D"),new Dictionary<string,int>{[GraylineSupplyRules.MaterialPrefix+"quartz"]=10},_=>0) with {VisitState="arrived"};
        var stock = supply.Stock[0];
        var pendingSupply = GraylineSupplyRules.Prepare(supply,Guid.NewGuid().ToString("D"),stock.ItemId,1,stock.UnitPrice,supply.DueDay,1000,[701]);
        var workshop = new WorkshopAchievementState { RunId=run,SlotId=slot,
            Completed=new(StringComparer.Ordinal){[CyberneticRules.InfraredVision]=new("nicokobo.aug.cybernetic.infrared_vision",801,1)},
            Claimed=[CyberneticRules.InfraredVision] };
        var original = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            [RunState.SaveKey]=" \n"+RunStateCodec.Write(ready)+"\t ",
            [StoryState.SaveKey]=StoryStateCodec.Write(story),
            [GraylineSupplyState.SaveKey]=GraylineSupplyCodec.Write(GraylineSupplyRules.Commit(pendingSupply)),
            [WorkshopAchievements.SaveKey]=WorkshopAchievements.Encode(workshop),
            ["other.mod:unchanged"]="  {\"keep\":1}  "
        };
        LoadedSlotBinding[] bindings=[new(RunState.SaveKey,RunForkMigration.Run),new(StoryState.SaveKey,RunForkMigration.Story),
            new(GraylineSupplyState.SaveKey,RunForkMigration.Supply),new(WorkshopAchievements.SaveKey,RunForkMigration.Workshop)];
        LoadedSlotPlan Plan(IReadOnlyDictionary<string,string>? data=null,int? target=null,string? nativeRun=null,
            int? day=null,int? startType=null,string? scenario=null) => OfficialLoadedSlotRebind.PrepareLoaded(
                data??original,nativeRun??run,target??slot+1,day??ready.LastObservedDay,
                startType??StartDefinition.NativeStartTypeId,scenario??StartDefinition.Id,bindings);
        Dictionary<string,string> Change(string key,Action<JsonObject> edit)
        { var copy=new Dictionary<string,string>(original);var value=JsonNode.Parse(copy[key])!.AsObject();edit(value);copy[key]=value.ToJsonString();return copy; }
        void Reject(string name,Dictionary<string,string> data,int? target=null,string? nativeRun=null,
            int? day=null,int? startType=null,string? scenario=null)
        {
            var before=new Dictionary<string,string>(data);bool rejected=false;
            try{Plan(data,target,nativeRun,day,startType,scenario);}
            catch(Exception ex) when(ex is InvalidDataException or InvalidOperationException or ArgumentException or AugMechanicalAscension.Serialization.JsonException){rejected=true;}
            Assert(rejected&&Same(before,data),name+": rejected without mutation");
        }
        Case("same-slot",()=>{
            int calls=0;var data=new Dictionary<string,string>(original);
            var observed=bindings.Select(b=>new LoadedSlotBinding(b.Key,(json,a,bSlot,c,d)=>{calls++;return b.Rebind(json,a,bSlot,c,d);})).ToArray();
            var same=OfficialLoadedSlotRebind.PrepareLoaded(data,run,slot,0,StartDefinition.NativeStartTypeId,StartDefinition.Id,observed);
            OfficialLoadedSlotRebind.Apply(same,data,()=>throw new Exception("Same-slot must not stage"));
            Assert(same.Writes.Length==0&&calls==0&&Same(original,data),"Same-slot preserves all loaded bytes without migration callbacks");
            var future=Change(RunState.SaveKey,json=>json["schemaVersion"]=999);future[StoryState.SaveKey]="opaque optional state";
            var before=new Dictionary<string,string>(future);
            Assert(Plan(future,slot).Writes.Length==0&&Same(future,before),"Same-slot future schema with known loaded address stays with ordinary readers");
            Reject("Missing source address cannot prove same-slot identity",new(original){[RunState.SaveKey]="{\"schemaVersion\":999}"},target:slot);
        });
        LoadedSlotPlan? validPlan=null;
        Case("copy",()=>{
            var data=new Dictionary<string,string>(original);var plan=Plan(data);validPlan=plan;
            Assert(plan.SourceSlot==slot&&plan.TargetSlot==slot+1&&plan.Writes.Length==4&&Same(original,data),"PrepareLoaded validates four source carriers without writes");
            foreach(var write in plan.Writes){
                var expected=JsonNode.Parse(write.Before)!;var actual=JsonNode.Parse(write.After)!;
                if(write.Key==WorkshopAchievements.SaveKey)expected["slotId"]=slot+1;
                else{expected["saveSlotId"]=slot+1;if(write.Key==RunState.SaveKey)expected["opening"]!["saveSlotId"]=slot+1;}
                Assert(JsonNode.DeepEquals(expected,actual),"Only native slot fields change, preserving receipts/UID/history: "+write.Key);
            }
            OfficialLoadedSlotRebind.Apply(plan,data,()=>true);
            Assert(plan.Writes.All(w=>data[w.Key]==w.After)&&data["other.mod:unchanged"]==original["other.mod:unchanged"],"Apply stages all four carriers and preserves outsiders");
            Assert(RunStateCodec.ReadForGame(data[RunState.SaveKey],run,slot+1).Status==ReadStatus.Ready&&
                StoryStateCodec.Read(data[StoryState.SaveKey],run,slot+1).Items[0].Uid==601&&
                GraylineSupplyCodec.Read(data[GraylineSupplyState.SaveKey],run,slot+1).Receipts[0].OutputUids[0]==701&&
                WorkshopAchievements.Decode(data[WorkshopAchievements.SaveKey],run,slot+1).Completed[CyberneticRules.InfraredVision].UniqueId==801,
                "Real destination codecs accept target identity and retain receipt identities");
        });
        foreach(var optional in bindings.Where(b=>b.Key!=RunState.SaveKey))Case("optional "+optional.Key,()=>{
            var data=new Dictionary<string,string>(original);data.Remove(optional.Key);var plan=Plan(data);OfficialLoadedSlotRebind.Apply(plan,data,()=>true);
            Assert(plan.Writes.Length==3&&!data.ContainsKey(optional.Key),"Absent optional carrier stays absent: "+optional.Key);
        });
        Case("core source",()=>{
            var unrelated=new Dictionary<string,string>{{"other.mod","raw"}};
            Assert(Plan(unrelated).Writes.Length==0&&unrelated.Count==1,"No Aug carrier means no migration or invented state");
            var orphan=new Dictionary<string,string>(original);orphan.Remove(RunState.SaveKey);
            Reject("Optional Aug states without core have no source identity",orphan);
        });
        Case("identity",()=>{
            Reject("Foreign native run",new(original),nativeRun:Guid.NewGuid().ToString("D"));
            Reject("Negative target slot",new(original),target:-1);Reject("Negative native day",new(original),day:-1);
            Reject("Wrong native start type",new(original),startType:StartDefinition.NativeStartTypeId+1);
            Reject("Wrong native scenario",new(original),scenario:"another.scenario");
            foreach(var binding in bindings){
                string key=binding.Key,rf=key==WorkshopAchievements.SaveKey?"runId":"gameRunId",sf=key==WorkshopAchievements.SaveKey?"slotId":"saveSlotId";
                Reject("Foreign carrier run "+key,Change(key,json=>json[rf]=Guid.NewGuid().ToString("D")));
                if(key!=RunState.SaveKey)Reject("Foreign optional slot "+key,Change(key,json=>json[sf]=slot+2));
                Reject("Future copied schema "+key,Change(key,json=>json["schemaVersion"]=999));
                Reject("Ambiguous canonical and prefixed carrier "+key,new(original){["nicokobo.aug:"+key]=original[key]});
            }
            Reject("Wrong Mod scenario",Change(RunState.SaveKey,json=>json["startId"]="another.scenario"));
            Reject("Duplicate loaded address field",new(original){[RunState.SaveKey]=original[RunState.SaveKey].Replace("\"saveSlotId\":"+slot,"\"saveSlotId\":"+slot+",\"saveSlotId\":"+slot)});
        });
        Case("pending",()=>{
            Reject("Pending core opening",new(original){[RunState.SaveKey]=RunStateCodec.Write(RunStateCodec.CreateNew(ready.BuildProfileId,run,slot,ready.StartDay,ready.ConfigSnapshot))});
            var ps=story with{Pending=new("unresolved","physical:copy",2,500,0,0)};StoryStateCodec.Read(StoryStateCodec.Write(ps),run,slot);
            Reject("Valid unresolved story operation",new(original){[StoryState.SaveKey]=StoryStateCodec.Write(ps)});
            GraylineSupplyCodec.Read(GraylineSupplyCodec.Write(pendingSupply),run,slot);
            Reject("Valid unresolved supply operation",new(original){[GraylineSupplyState.SaveKey]=GraylineSupplyCodec.Write(pendingSupply)});
            var pw=WorkshopAchievements.Prepare(workshop with{Claimed=[]},CyberneticRules.InfraredVision,500,[new("metal_scanner",802,1)]);
            WorkshopAchievements.Decode(WorkshopAchievements.Encode(pw),run,slot);
            Reject("Valid unresolved workshop delivery",new(original){[WorkshopAchievements.SaveKey]=WorkshopAchievements.Encode(pw)});
        });
        Case("CAS",()=>{
            var plan=validPlan??throw new InvalidOperationException("Valid plan fixture missing");
            var before=new InterceptingDictionary(original);before.RawSet(plan.Writes[0].Key,"foreign-before");
            Assert(Throws(()=>OfficialLoadedSlotRebind.Apply(plan,before,()=>true))&&before.Writes==0,"Pre-Apply byte conflict prevents first write");
            var address=new InterceptingDictionary(original);
            Assert(Throws(()=>OfficialLoadedSlotRebind.Apply(plan,address,()=>false))&&address.Writes==0,"Changed native address prevents first write");
            bool live=true;int forward=0;var moved=new InterceptingDictionary(original);
            moved.OnSet=(key,value)=>{if(plan.Writes.Any(w=>w.Key==key&&w.After==value)){forward++;live=false;}};
            Assert(Throws(()=>OfficialLoadedSlotRebind.Apply(plan,moved,()=>live))&&Same(original,moved)&&forward==1,"Midwrite address change stops next write and rolls back");
            bool once=false;var mid=new InterceptingDictionary(original);
            mid.OnSet=(key,value)=>{if(!once&&key==plan.Writes[0].Key&&value==plan.Writes[0].After){once=true;mid.RawSet(plan.Writes[1].Key,"foreign-mid");}};
            Assert(Throws(()=>OfficialLoadedSlotRebind.Apply(plan,mid,()=>true))&&mid[plan.Writes[0].Key]==original[plan.Writes[0].Key]&&
                mid[plan.Writes[1].Key]=="foreign-mid"&&plan.Writes.Skip(2).All(w=>mid[w.Key]==w.Before),"Per-write CAS preserves concurrent value and rolls back completed writes");
            bool replaced=false;var post=new InterceptingDictionary(original);
            post.OnSet=(key,value)=>{if(!replaced&&key==plan.Writes[1].Key&&value==plan.Writes[1].After){replaced=true;post.RawSet(plan.Writes[0].Key,"foreign-after");}};
            Assert(Throws(()=>OfficialLoadedSlotRebind.Apply(plan,post,()=>true))&&post[plan.Writes[0].Key]=="foreign-after"&&plan.Writes.Skip(1).All(w=>post[w.Key]==w.Before),"Rollback does not overwrite another writer");
            bool thrown=false;var broken=new InterceptingDictionary(original);
            broken.OnSet=(key,value)=>{if(!thrown&&key==plan.Writes[1].Key&&value==plan.Writes[1].After){thrown=true;throw new IOException("injected after write");}};
            Assert(Throws(()=>OfficialLoadedSlotRebind.Apply(plan,broken,()=>true))&&Same(original,broken),"Setter failure after mutation rolls back own writes");
        });
        foreach(var result in results)Console.WriteLine((result.Passed?"PASS ":"FAIL ")+result.Name);
        foreach(var result in results)check(result.Passed,result.Name);
    }
    private static bool Throws(Action action)
    {try{action();return false;}catch(Exception ex) when(ex is IOException or InvalidDataException){return true;}}
    // Reentrancy injection only; the source-linked Apply owns every decision.
    private sealed class InterceptingDictionary(IReadOnlyDictionary<string,string> source):Dictionary<string,string>(source),IDictionary<string,string>
    {
        internal Action<string,string>? OnSet;internal int Writes;
        internal void RawSet(string key,string value)=>base[key]=value;
        string IDictionary<string,string>.this[string key]
        {get=>base[key];set{Writes++;base[key]=value;OnSet?.Invoke(key,value);}}
    }
}
