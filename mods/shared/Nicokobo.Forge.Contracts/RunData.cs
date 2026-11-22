namespace Nicokobo.ContentInterop;
public enum RunDataState { Disabled, Invalid, Unavailable, Missing, Present, StagedInMemory, Conflict, Indeterminate }
public sealed record RunDataValue(RunDataState Status, string? Json = null, string? Reason = null);
