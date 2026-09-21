namespace ArquitecturaBase.Application.Features.Users.GetUser;

/// <summary>El usuario con sus roles: lo que necesita el diálogo de edición (sección 10 del spec de la Fase 4).</summary>
public sealed record UserDetail(
    Guid Id,
    string Email,
    string? DisplayName,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<string> Roles);
