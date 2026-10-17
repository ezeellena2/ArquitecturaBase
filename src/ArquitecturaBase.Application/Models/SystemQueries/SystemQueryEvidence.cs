namespace ArquitecturaBase.Application.Models.SystemQueries;

public sealed record SystemQueryEvidence(Guid Id, string SourceId, int SourceVersion, DateTime ObservedAtUtc,
    long? Revision, string? Destination, IReadOnlyList<string> RequiredPermissions,
    IReadOnlyList<SystemQueryRow> Rows, bool IsComplete = true)
{
    public override string ToString() => "SystemQueryEvidence { content: [REDACTED] }";
}

public sealed record SystemQueryRow(Guid? Id, IReadOnlyList<SystemQueryValue> Values)
{
    public override string ToString() => "SystemQueryRow { content: [REDACTED] }";
}
public sealed record SystemQueryValue(string Label, string Value)
{
    public override string ToString() => "SystemQueryValue { content: [REDACTED] }";
}
