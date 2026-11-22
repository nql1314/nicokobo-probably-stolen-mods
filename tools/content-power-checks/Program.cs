using Nicokobo.ContentInterop;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
void Throws(Action action, string message)
{
    try { action(); } catch (InvalidOperationException) { checks++; return; }
    throw new Exception(message);
}

var source = new GameItem();
int budget = 100, debits = 0;
Check(!DevicePower.TryEnergy(source, out int absent) && absent == 0, "Unclaimed source must keep native fallback");
Check(!DevicePower.TryDebit(source, 5, out bool absentDebit) && !absentDebit, "Unclaimed debit must not claim success");
DevicePower.Register("logistics", item => item == source ? budget : null, (item, cost) =>
{
    if (item != source) return null;
    debits++;
    if (cost > budget) return false;
    budget -= cost;
    return true;
});
Check(DevicePower.TryEnergy(source, out int energy) && energy == 100, "Network availability must be authoritative");
Check(DevicePower.TryDebit(source, 25, out bool committed) && committed, "Successful network debit must commit");
Check(budget == 75 && debits == 1, "A committed request debits exactly once");
Check(DevicePower.TryDebit(source, 76, out bool insufficient) && !insufficient, "Insufficient shared energy rejects without native fallback");
Check(budget == 75 && debits == 1, "Preflight rejection must not mutate resources");
DevicePower.Register("rejecting", item => item == source ? 100 : null, (_, _) => { debits++; return true; });
Throws(() => DevicePower.TryDebit(source, 1, out _), "Ambiguous owners must be rejected before any debit");
Check(budget == 75 && debits == 1, "Provider collision must not spend resources");
DevicePower.Remove("rejecting");
DevicePower.Register("logistics", item => item == source ? budget : null, (_, _) => { debits++; return false; });
Check(DevicePower.TryDebit(source, 10, out bool rejected) && !rejected, "Provider rejection remains authoritative");
Check(budget == 75 && debits == 2, "Failed provider request cannot fall through and double charge");
DevicePower.Register("logistics", item => item == source ? 1 : null, (_, _) => null);
Throws(() => DevicePower.TryDebit(source, 1, out _), "Claimed sources require a definite transaction result");
DevicePower.Remove("logistics");
Check(!DevicePower.TryEnergy(source, out _), "Owner detach restores native fallback");
DevicePower.Register("invalid", _ => -1, (_, _) => true);
Throws(() => DevicePower.TryEnergy(source, out _), "Negative availability must be rejected");
DevicePower.Remove("invalid");
Console.WriteLine($"PASS: {checks} shared power ownership and atomic debit checks");

public sealed class GameItem { public string identifier = ""; }
namespace Nicokobo.ContentInterop
{
    internal static class ContentRuntime
    {
        internal static void RequireOwner(string owner)
        { if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException(nameof(owner)); }
    }
}
