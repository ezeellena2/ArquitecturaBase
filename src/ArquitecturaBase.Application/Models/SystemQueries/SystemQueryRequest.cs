namespace ArquitecturaBase.Application.Models.SystemQueries;

public sealed record SystemQueryRequest(Guid ActorUserId, string ToolId, string Culture, string TimeZoneId,
    Guid? RoleId = null, string? RoleName = null, string? HelpTopic = null)
{
    public override string ToString() => "SystemQueryRequest { arguments: [REDACTED] }";
}
