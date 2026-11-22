using Newtonsoft.Json;

namespace AugMechanicalAscension.Domain;

internal enum OpeningEconomyApplyStatus
{
    Applied, AlreadyMatched, IdentityMismatch, InvalidPhase,
    UnverifiedBoundary, Unavailable,
    PlanRejected, Partial, Indeterminate
}

internal sealed record OpeningEconomyApplyResult(OpeningEconomyApplyStatus Status,
    OpeningEconomySnapshot? Before = null, OpeningEconomySnapshot? After = null,
    bool CashAttempted = false, bool RentAttempted = false,
    bool ReputationAttempted = false, string? Error = null,
    [property: JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    int? SecurityReputationTarget = null,
    [property: JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    int? CashTarget = null);

// Every read and write comes from the native adapter. A failed postcondition
// records what was attempted and leaves recovery to an explicit later step.
internal interface IOpeningEconomyPort
{
    OpeningEconomySnapshot? Read();
    void AddCash(int delta);
    void SetRent(int value);
    void AddSecurityReputation(int delta);
}

internal static class OpeningEconomyTransaction
{
    internal static OpeningEconomyApplyResult Apply(IOpeningEconomyPort port,
        ConfigSnapshot config)
    {
        bool cashAttempted = false, rentAttempted = false, reputationAttempted = false;
        OpeningEconomySnapshot? before = null;
        try
        {
            before = port.Read();
            if (before?.NativeReadVerified != true)
                return new(OpeningEconomyApplyStatus.Unavailable);
            var plan = OpeningEconomyRules.Plan(config, before);
            if (plan.Status != OpeningEconomyStatus.Ready)
                return new(OpeningEconomyApplyStatus.PlanRejected, before,
                    Error: plan.Status.ToString());
            if (OpeningEconomyRules.Matches(config, before, plan.SecurityReputationTarget, plan.CashTarget))
                return new(OpeningEconomyApplyStatus.AlreadyMatched, before, before,
                    SecurityReputationTarget: plan.SecurityReputationTarget, CashTarget: plan.CashTarget);

            if (plan.CashDelta != 0)
            {
                cashAttempted = true;
                port.AddCash(plan.CashDelta);
            }
            var current = port.Read();
            if (current?.NativeReadVerified != true ||
                current.Credits != plan.CashTarget)
                return Partial(port, before, cashAttempted, rentAttempted,
                    reputationAttempted, "Cash postcondition failed");

            if (current.Rent != plan.RentTarget)
            {
                rentAttempted = true;
                port.SetRent(plan.RentTarget);
                current = port.Read();
                if (current?.NativeReadVerified != true ||
                    current.Rent != config.InitialRent ||
                    current.Credits != plan.CashTarget)
                    return Partial(port, before, cashAttempted, rentAttempted,
                        reputationAttempted, "Rent postcondition failed");
            }

            var freshPlan = OpeningEconomyRules.Plan(config, current, plan.CashTarget);
            if (freshPlan.Status != OpeningEconomyStatus.Ready ||
                freshPlan.CashDelta != 0 || current.Rent != config.InitialRent)
                return Partial(port, before, cashAttempted, rentAttempted,
                    reputationAttempted, "Economy changed during application");
            if (freshPlan.SecurityReputationDelta != 0)
            {
                reputationAttempted = true;
                port.AddSecurityReputation(freshPlan.SecurityReputationDelta);
            }
            var after = port.Read();
            if (after == null || !OpeningEconomyRules.Matches(config, after,
                    freshPlan.SecurityReputationTarget, plan.CashTarget))
                return Partial(port, before, cashAttempted, rentAttempted,
                    reputationAttempted, "Opening economy postcondition failed");
            return new(OpeningEconomyApplyStatus.Applied, before, after,
                cashAttempted, rentAttempted, reputationAttempted,
                SecurityReputationTarget: freshPlan.SecurityReputationTarget, CashTarget: plan.CashTarget);
        }
        catch (Exception ex)
        {
            if (!cashAttempted && !rentAttempted && !reputationAttempted)
                return new(OpeningEconomyApplyStatus.Unavailable, before,
                    Error: ex.GetType().Name);
            return Partial(port, before, cashAttempted, rentAttempted,
                reputationAttempted, ex.GetType().Name);
        }
    }

    private static OpeningEconomyApplyResult Partial(IOpeningEconomyPort port,
        OpeningEconomySnapshot? before, bool cashAttempted, bool rentAttempted,
        bool reputationAttempted, string error)
    {
        try
        {
            var after = port.Read();
            if (after?.NativeReadVerified != true)
                return new(OpeningEconomyApplyStatus.Indeterminate, before,
                    CashAttempted: cashAttempted, RentAttempted: rentAttempted,
                    ReputationAttempted: reputationAttempted, Error: error);
            return new(OpeningEconomyApplyStatus.Partial, before, after,
                cashAttempted, rentAttempted, reputationAttempted, error);
        }
        catch
        {
            return new(OpeningEconomyApplyStatus.Indeterminate, before,
                CashAttempted: cashAttempted, RentAttempted: rentAttempted,
                ReputationAttempted: reputationAttempted, Error: error);
        }
    }
}

