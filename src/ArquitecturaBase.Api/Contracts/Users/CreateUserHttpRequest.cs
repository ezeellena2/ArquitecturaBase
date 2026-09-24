namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>El cuerpo del alta administrativa de una cuenta.</summary>
public sealed record CreateUserHttpRequest(
    string? Email,
    string? DisplayName,
    IReadOnlyCollection<string>? Roles,
    PhoneNumberHttpRequest? Phone = null,
    InvitationHttpRequest? Invitation = null)
{
    // MVC registra los argumentos de la acción con ToString(): no exponer correo ni número completo.
    public override string ToString() => nameof(CreateUserHttpRequest);
}
