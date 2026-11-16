namespace ArquitecturaBase.Api.Contracts.Roles;

/// <summary>
/// El cuerpo de PUT /api/roles/{id}; el id llega por la ruta. <c>ExpectedRevision</c> es la <c>revision</c> del rol que se
/// editó: si cambió en el medio, responde 409 <c>Roles.Role.RevisionConflict</c> (N2).
/// </summary>
public sealed record UpdateRoleHttpRequest(
    string? Name, string? Description, IReadOnlyCollection<string>? Permissions, long ExpectedRevision);
