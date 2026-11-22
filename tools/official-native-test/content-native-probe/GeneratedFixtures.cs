using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    // Fixture setup is intentionally separate from the transaction/lifecycle
    // under assertion. No achievement, claim, receipt or result flag is set.
    private void FixtureClearVisitors(string purpose)
    {
        EmporiumOption.Instance.autoCallNext = false;
        EmporiumOption.Instance.autoNextDialog = true;
        EmporiumOption.Instance.enableTypewriterEffect = false;
        EmporiumOption.Instance.instantDismissCustomer = true;
        var queue = Store.storeClientManager.clientStack;
        var input = new JObject { ["purpose"] = purpose, ["removedQueue"] = new JArray(queue.Select(client => client.identifier)),
            ["removedCurrent"] = Store.currentClientInstance?.GetClientBlueprint()?.identifier };
        queue.Clear();
        if (Store.currentClientInstance != null) Store.DismissCurrentClient(false);
        Store.callNextWhenPossible = false;
        if (R.environment.TryGetValue("generatedVisitorFixtures", out var value) && value is JArray rows) rows.Add(input);
        else R.environment["generatedVisitorFixtures"] = new JArray(input);
    }

    private IEnumerable<object?> FixturePrepareNight()
    {
        FixtureClearVisitors("night input; no natural customers or fund raising");
        int invoice = ReputationTonightInvoice();
        if (Store.playerCash < invoice + 2000)
        {
            int before = Store.playerCash;
            Store.ModCash(20000 - before, false);
            _forgeFixtures.Add(JObject.FromObject(new { purpose = "explicit service-payment test funds", before, after = Store.playerCash }));
        }
        yield return null;
        ForgeRequire("fixture.night-no-natural-customers", "Empty fixture queue and no current customer; actual native close eligibility",
            new { queued = Store.storeClientManager.clientStack.Count, current = Store.currentClientInstance != null, canEnd = Store.CanEndDay() },
            Store.storeClientManager.clientStack.Count == 0 && Store.currentClientInstance == null && Store.CanEndDay());
    }
}
