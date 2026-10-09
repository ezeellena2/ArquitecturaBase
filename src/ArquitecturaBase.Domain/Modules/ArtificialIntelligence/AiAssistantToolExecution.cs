using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public sealed class AiAssistantToolExecution : Entity, IAuditable
{
    private AiAssistantToolExecution() { }
    public Guid TurnId { get; private set; }
    public int Ordinal { get; private set; }
    public string ToolCallId { get; private set; } = string.Empty;
    public string ToolId { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public string[] RequiredPermissions { get; private set; } = [];
    public string EvidenceId { get; private set; } = string.Empty;
    public DateTime ObservedAtUtc { get; private set; }
    public string? ProtectedResult { get; private set; }
    public int Bytes { get; private set; }
    public string State { get; private set; } = "complete";
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static AiAssistantToolExecution Create(Guid turnId, int ordinal, string callId, string toolId, int version,
        string[] permissions, string evidenceId, DateTime observedAtUtc, string protectedResult, int bytes, string state = "complete") => new()
    {
        TurnId = turnId, Ordinal = ordinal, ToolCallId = callId, ToolId = toolId, Version = version,
        RequiredPermissions = permissions.ToArray(), EvidenceId = evidenceId, ObservedAtUtc = observedAtUtc,
        ProtectedResult = protectedResult, Bytes = bytes, State = state,
    };
    public override string ToString() => "AiAssistantToolExecution { result: [REDACTED] }";
}
