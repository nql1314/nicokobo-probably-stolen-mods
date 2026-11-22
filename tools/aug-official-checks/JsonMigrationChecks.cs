using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using AugMechanicalAscension.Serialization;
internal static class JsonMigrationChecks
{
    internal static int Run()
    {
        int count=0;string run="10000000-0000-0000-0000-000000000001";
        void Check(bool ok,string reason){count++;if(!ok)throw new Exception("JSON migration: "+reason);}
        string Fixture(string name)=>File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"golden",name));
        string oldRun=Fixture("legacy-run.json");
        var read=RunStateCodec.ReadForGame(oldRun,run,7);
        Check(read.Status==ReadStatus.Ready,"Legacy System.Text.Json run/config hash must be accepted");
        Check(RunStateCodec.Read(RunStateCodec.Write(read.State!)).Status==ReadStatus.Ready,"Legacy run reserializes without losing validation");
        var story=StoryStateCodec.Read(Fixture("legacy-story.json"),run,7);
        Check(StoryStateCodec.Write(StoryStateCodec.Read(StoryStateCodec.Write(story),run,7))==StoryStateCodec.Write(story),"Legacy story roundtrip");
        var supply=GraylineSupplyCodec.Read(Fixture("legacy-supply.json"),run,7);
        Check(GraylineSupplyCodec.Read(GraylineSupplyCodec.Write(supply),run,7).Revision==supply.Revision,"Legacy supply roundtrip");
        var workshop=WorkshopAchievements.Decode(Fixture("legacy-workshop.json"),run,7);
        Check(WorkshopAchievements.Decode(WorkshopAchievements.Encode(workshop),run,7).Claimed.Length==0,"Legacy workshop roundtrip");
        foreach(string invalid in new[]{"{'a':1}","{a:1}","{\"a\":NaN}","{\"a\":01}","{\"a\":+1}","{\"a\":0x1}","{\"a\":1,}","[1,]","{\"a\":1}//comment","{\"a\":1,\"a\":2}","{\"a\":\"line\nfeed\"}"})
        {
            bool rejected=false;try{using var doc=JsonDocument.Parse(invalid);}catch(JsonException){rejected=true;}
            Check(rejected,"Reject nonstandard/ambiguous syntax: "+invalid);
        }
        foreach(string invalid in new[]{"{\"value\":1.2}","{\"value\":\"1\"}","{\"value\":2147483648}","{\"value\":null}"})
        {
            bool rejected=false;try{JsonSerializer.Deserialize<IntegerValue>(invalid,new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase});}catch(JsonException){rejected=true;}
            Check(rejected,"Never coerce non-integer or overflow to a saved integer");
        }
        Check(JsonSerializer.Deserialize<IntegerValue>("{\"Value\":2}",new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase})!.Value==0,"Property names retain case-sensitive legacy behavior");
        Console.WriteLine($"JSON migration checks passed: {count}");return count;
    }
    private sealed record IntegerValue(int Value);
}
