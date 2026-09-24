namespace ArquitecturaBase.Api.Contracts.Roles;

/// <summary>El cuerpo de POST /api/roles.</summary>
public sealed record CreateRoleHttpRequest(string? Name, string? Description, IReadOnlyCollection<string>? Permissions);
