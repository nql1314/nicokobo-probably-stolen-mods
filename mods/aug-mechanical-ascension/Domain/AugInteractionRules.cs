namespace AugMechanicalAscension.Domain;

internal enum AugAction { Threat, Kill }

// One native visitor can be threatened and then killed. Keep both marks in the
// same saved receipt so repeated callbacks or a reload cannot charge twice.
internal sealed record AugInteractionReceipt(string RunId, int Day,
    int VisitsLeft, string ClientId, bool Threatened = false, bool Killed = false);

internal static class AugInteractionRules
{
    internal static int ClampPresence(int presence)
        => Math.Clamp(presence, AugNumbers.AugInteractions.MinimumPresence,
            AugNumbers.AugInteractions.MaximumPresence);

    internal static RunState? ApplyAction(RunState state, AugAction action,
        bool mechanicalAscension = false)
        => ExposureVisitRules.ApplyGain(state, action switch
        {
            AugAction.Threat => AugNumbers.AugInteractions.ThreatExposureGain,
            AugAction.Kill => AugNumbers.AugInteractions.KillExposureGain,
            _ => 0
        }, mechanicalAscension);

    internal static AugInteractionReceipt? RecordAction(
        AugInteractionReceipt? previous, AugAction action, string runId,
        int day, int visitsLeft, string clientId)
    {
        var receipt = previous != null && previous.RunId == runId &&
                      previous.Day == day && previous.VisitsLeft == visitsLeft &&
                      previous.ClientId == clientId
            ? previous
            : new AugInteractionReceipt(runId, day, visitsLeft, clientId);
        return action switch
        {
            AugAction.Threat when !receipt.Threatened && !receipt.Killed
                => receipt with { Threatened = true },
            AugAction.Kill when !receipt.Killed
                => receipt with { Killed = true },
            _ => null
        };
    }
}
