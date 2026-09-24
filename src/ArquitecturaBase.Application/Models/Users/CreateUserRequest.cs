namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Alta administrativa; correo o teléfono obligatorio, roles User por defecto.</summary>
public sealed record CreateUserRequest(
    string? Email,
    string? DisplayName,
    IReadOnlyCollection<string>? Roles,
    PhoneNumberInput? Phone = null,
    InvitationRequest? Invitation = null);
