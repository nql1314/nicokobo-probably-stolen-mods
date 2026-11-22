namespace DefinitelyStolen.Domain;

internal enum CommitResult { Committed, Restored, Failed }

internal static class CommitRules
{
    internal static CommitResult Execute(Action apply, Action validate, Action undo,
        Action finish, Action<Exception> report)
    {
        try { apply(); validate(); }
        catch (Exception reason)
        {
            try { undo(); }
            catch (Exception ex) { report(new AggregateException(reason, ex)); return CommitResult.Failed; }
            report(reason);
            return CommitResult.Restored;
        }

        // The live result is final once validated. Cleanup failure cannot
        // replay the award or refund its action cost.
        try { finish(); }
        catch (Exception ex) { report(ex); return CommitResult.Failed; }
        return CommitResult.Committed;
    }
}
