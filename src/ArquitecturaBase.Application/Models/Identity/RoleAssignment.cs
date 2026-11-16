namespace ArquitecturaBase.Application.Models.Identity;

/// <summary>Los roles que una asignación le agregó y le quitó a una cuenta, por nombre; vacíos si quedó igual.</summary>
public sealed record RoleAssignment(IReadOnlyCollection<string> Added, IReadOnlyCollection<string> Removed);
