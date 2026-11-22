using System.Text.Json;
using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;

int assertions = 0;
void Check(bool result, string name) { assertions++; if (!result) throw new Exception(name); }
void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
var catalog = Catalog.Load();
Check(catalog.Enabled.Count == 33 && catalog.DropIds.Count == 147, "Six new occupations and nineteen native item candidates are available");
Check(catalog.Regions.Select(x => x.Roles.Sum(role => role.Weight)).SequenceEqual(new[] { 121, 123, 113 }), "New regional weights retain existing relative weights and add the approved occupations");
int unique = 100;
var referenceValues = catalog.Items.ToDictionary(x => x.Id, x => x.ReferenceValue);
string Graph(string id, int uid) => JsonSerializer.Serialize(new { saveItems = new[] { new { identifier = id, uniqueId = uid, unitCount = 1 } } });
long ReferenceValue(string id, int tier, StableRandom random)
{
    long value = referenceValues[id];
    if (id == "energy_credit") value = 10 + new[] { 0, 25, 50, 75, 100 }[Step(random.Unit())];
    if (id == "energy_credit_ext") value = 30 + new[] { 0, 62, 125, 187, 250 }[Step(random.Unit())];
    if (id == "energy_credit_breeder") value = 400 + new[] { 0, 25, 50, 75, 100 }[Step(random.Unit())];
    if (id == "packet_red_cigarette") value = random.Index(101) <= 30 ? 100 : 50;
    if (id == "large_purple_injector") value = 160;
    if (id == "water_test_container") value = 31;
    if (id == "water_tablet_container") value = 55;
    if (id is "mini_bottle" or "small_bottled_water" or "bottled_water" or "large_bottled_water")
    {
        double roll = random.Unit(); int grade = tier switch
        { 1 => roll < .75 ? 2 : roll < .95 ? 1 : 0, 2 => roll < .50 ? 2 : roll < .90 ? 1 : 0, _ => roll < .20 ? 2 : roll < .70 ? 1 : 0 };
        value = (id switch { "mini_bottle" => new[] { 23,18,13 }, "small_bottled_water" => new[] { 40,31,22 },
            "bottled_water" => new[] { 65,50,35 }, _ => new[] { 169,129,89 } })[grade];
    }
    return value;
}
ItemState Make(string id, int tier, StableRandom random)
{
    long value = ReferenceValue(id, tier, random);
    int uid = unique++; var item = new ItemState { Id = id, Value = value, UniqueId = uid, Graph = Graph(id, uid) };
    if (CandidateState.NeedsNativeState(id)) item.NativeStateVersion = 1;
    if (id is "energy_credit" or "energy_credit_ext" or "energy_credit_breeder")
    {
        item.MaxCharge = id == "energy_credit_ext" ? 250 : 100;
        item.Charge = (int)(value - (id == "energy_credit" ? 10 : id == "energy_credit_ext" ? 30 : 400));
    }
    if (CandidateState.IsWater(id))
    {
        item.VolumeMl = item.CapacityMl = id switch { "mini_bottle" => 250, "small_bottled_water" => 450, "bottled_water" => 750, _ => 2000 };
        item.Purity = 10000;
        item.LiquidParts = new() { ["CURRENT_PART_WATER"] = item.VolumeMl.Value * 1000 };
    }
    if (id == "packet_red_cigarette") item.Condition = value == 100 ? "genuine" : "counterfeit";
    if (CandidateState.IsFish(id)) item.Condition = id == "sturgeon" || random.Index(2) == 0 ? "FEMALE" : "MALE";
    TheftRules.Classify(item); return item;
}
int Step(double roll) => roll < .15 ? 0 : roll < .30 ? 1 : roll < .60 ? 2 : roll < .85 ? 3 : 4;
RunState Run(int level = 0, ulong seed = 12345)
{
    var run = new RunState { PreferredMode = TheftMode.Probability, RunId = "test-run-" + seed, SlotId = 1, Qualified = true, QualificationSource = "perk", Experience = TheftRules.Threshold(level), RandomState = seed };
    run.Night = GenerationRules.Night(run, 1, catalog, Make, (role, rng) => role.SpriteGroup + rng.Index(3)); return run;
}

SceneLayoutChecks.Run(Check);
NightContentChecks.Run(catalog, Run, Make, Check, Reject);
SpecialNpcChecks.Run(catalog, Run, Make, Check);
CandidatePersistenceChecks.Run(catalog, Run, Check, Reject);
FailureScopeChecks.Run(catalog, Run, Check, Reject);
CustodyOwnershipChecks.Run(Check);
TimingChecks.Run(catalog, Run, Check, Reject);
OfficialPersistenceChecks.Run(catalog, Run, Check, Reject);
SmokeDomainChecks.Run(catalog, Run, Check, Reject);
PerkChecks.Run(Check);
PackageContractChecks.Run(args, Check);
Console.WriteLine($"PASS {assertions} official migration checks (offline; no native game execution).");
