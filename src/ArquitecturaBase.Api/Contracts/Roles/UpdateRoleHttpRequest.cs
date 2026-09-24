namespace ArquitecturaBase.Api.Contracts.Roles;

/// <summary>El cuerpo de PUT /api/roles/{id}; el id llega por la ruta.</summary>
public sealed record UpdateRoleHttpRequest(string? Name, string? Description, IReadOnlyCollection<string>? Permissions);
