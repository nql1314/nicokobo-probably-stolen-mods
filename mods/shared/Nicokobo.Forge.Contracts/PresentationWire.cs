namespace Nicokobo.ContentInterop;

internal static partial class ForgeWire
{
    internal static object[] ConversationContext(OfficialDialogueContext c) => new object[] { c.Generation, c.RunId, c.SlotId, c.OwnerId, c.RequestId, c.DialogueId, c.ChoiceId };
    internal static OfficialDialogueContext ConversationContext(object[] c) => new((long)c[0], (string)c[1], (int)c[2], (string)c[3], (string)c[4], (string)c[5], (string)c[6]);
    internal static object[] Script(OfficialDialogueScript s) => new object[] { s.OwnerId, Dialogue(s.Definition) };
    internal static OfficialDialogueScript Script(object[] s) => new((string)s[0], Dialogue((object[])s[1]));
    internal static object[] Dialogue(OfficialDialogueDefinition d) => new object[] { d.Id, d.Speaker, d.Body,
        d.Choices.Select(c => new object[] { c.Id, c.Label, c.Preview, (Func<object[], string>)(v => c.Confirm(ConversationContext(v))) }).ToArray(),
        d.ConfirmLabel, d.BackLabel, d.LeaveLabel, d.LaterLabel, d.FailureText, d.RequireConfirmation, d.AllowLater, d.NextPageLabel, d.PreviousPageLabel };
    internal static OfficialDialogueDefinition Dialogue(object[] d) => new((string)d[0], (string)d[1], (string)d[2],
        ((object[][])d[3]).Select(c => new OfficialDialogueChoice((string)c[0], (string)c[1], (string)c[2], context => ((Func<object[], string>)c[3])(ConversationContext(context)))).ToArray(),
        (string)d[4], (string)d[5], (string)d[6], (string)d[7], (string)d[8])
        { RequireConfirmation = (bool)d[9], AllowLater = (bool)d[10], NextPageLabel = (string)d[11], PreviousPageLabel = (string)d[12] };
    internal static object[] SupplierPage(OfficialSupplierPage p) => new object[] { p.Body,
        p.Offers.Select(o => new object[] { o.Id, o.Label, o.Preview, (Func<object[], string>)(v => o.Purchase(ConversationContext(v))), o.ConfirmLabel!, o.Enabled }).ToArray(),
        p.AutomaticOfferId!, p.AutomaticDelaySeconds };
    internal static OfficialSupplierPage SupplierPage(object[] p) => new((string)p[0],
        ((object[][])p[1]).Select(o => new OfficialSupplierOffer((string)o[0], (string)o[1], (string)o[2], c => ((Func<object[], string>)o[3])(ConversationContext(c)), (string?)o[4]) { Enabled = (bool)o[5] }).ToArray())
        { AutomaticOfferId = (string?)p[2], AutomaticDelaySeconds = (double)p[3] };
    internal static object[] Supplier(OfficialSupplierDefinition s) => new object[] { s.Id, s.Speaker,
        (Func<object[], object[]>)(v => SupplierPage(s.Capture(ConversationContext(v)))), (Action<object[]>)(v => s.Dismiss(ConversationContext(v))),
        s.ConfirmLabel, s.BackLabel, s.ContinueLabel, s.LeaveLabel, s.FailureText, s.RequireConfirmation, s.AllowMenuRefresh,
        s.CanContinue == null ? null! : (Func<object[], bool>)(v => s.CanContinue(ConversationContext(v))), s.NextPageLabel, s.PreviousPageLabel };
    internal static OfficialSupplierDefinition Supplier(object[] s) => new((string)s[0], (string)s[1], c => SupplierPage(((Func<object[], object[]>)s[2])(ConversationContext(c))),
        c => ((Action<object[]>)s[3])(ConversationContext(c)), (string)s[4], (string)s[5], (string)s[6], (string)s[7], (string)s[8])
        { RequireConfirmation = (bool)s[9], AllowMenuRefresh = (bool)s[10], CanContinue = s[11] is Func<object[], bool> more ? c => more(ConversationContext(c)) : null,
            NextPageLabel = (string)s[12], PreviousPageLabel = (string)s[13] };
    private static object[] PhoneContext(OfficialPhoneContext c) => new object[] { c.Generation, c.RunId, c.SlotId };
    private static OfficialPhoneContext PhoneContext(object[] c) => new((long)c[0], (string)c[1], (int)c[2]);
    internal static object[] Phone(OfficialPhoneContact c) => new object[] { c.Id, c.Number,
        (Func<object[], bool>)(v => c.Visible(PhoneContext(v))), (Func<object[], string>)(v => c.DisplayName(PhoneContext(v))),
        (Func<object[], object[]>)(v => Script(c.Call(PhoneContext(v)))) };
    internal static OfficialPhoneContact Phone(object[] c) => new((string)c[0], (long)c[1], v => ((Func<object[], bool>)c[2])(PhoneContext(v)),
        v => ((Func<object[], string>)c[3])(PhoneContext(v)), v => Script(((Func<object[], object[]>)c[4])(PhoneContext(v))));
    internal static object[] PresentationResult(OfficialPresentationResult r) => new object[] { (int)r.Status, r.Reason! };
    internal static OfficialPresentationResult PresentationResult(object[] r) => new((OfficialPresentationStatus)(int)r[0], (string?)r[1]);
    internal static object[] Workshop(OfficialWorkshopSnapshot s) => new object[] { s.Credits, s.English,
        s.Groups.Select(g => new object[] { g.ChineseTitle, g.EnglishTitle, g.EntryCount }).ToArray(),
        s.Entries.Select(e => new object[] { e.Id, e.Title, e.Subtitle, e.Description, e.Requirement, e.CostText, e.NodeCostText, e.StateText, e.ActionText,
            e.Unlocked, e.PrerequisiteMet, e.ActionEnabled, e.Dependencies.ToArray(),
            e.Conditions.Select(c => new object[] { (int)c.Kind, c.ResourceId, c.Quantity, c.Consume, c.Satisfied, c.DisplayName! }).ToArray(),
            e.Rewards.Select(r => new object[] { (int)r.Kind, r.ResourceId, r.Quantity, r.DisplayName! }).ToArray(),
            e.Completed!, e.VisualLinks.ToArray(), e.GraphX!, e.GraphY! }).ToArray(), s.Message };
    internal static OfficialWorkshopSnapshot Workshop(object[] s) => new((int)s[0], (bool)s[1],
        ((object[][])s[2]).Select(g => new OfficialWorkshopGroup((string)g[0], (string)g[1], (int)g[2])).ToArray(),
        ((object[][])s[3]).Select(e => new OfficialWorkshopEntry((string)e[0], (string)e[1], (string)e[2], (string)e[3], (string)e[4], (string)e[5], (string)e[6], (string)e[7], (string)e[8], (bool)e[9], (bool)e[10], (bool)e[11])
        {
            Dependencies = (string[])e[12],
            Conditions = ((object[][])e[13]).Select(c => new OfficialWorkshopRequirement((OfficialWorkshopResourceKind)(int)c[0], (string)c[1], (int)c[2], (bool)c[3], (bool)c[4]) { DisplayName = (string?)c[5] }).ToArray(),
            Rewards = ((object[][])e[14]).Select(r => new OfficialWorkshopReward((OfficialWorkshopResourceKind)(int)r[0], (string)r[1], (int)r[2]) { DisplayName = (string?)r[3] }).ToArray(),
            Completed = (bool?)e[15], VisualLinks = (string[])e[16], GraphX = (float?)e[17], GraphY = (float?)e[18]
        }).ToArray(), (string)s[4]);
}
