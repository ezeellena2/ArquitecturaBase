namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>El cuerpo de PUT /api/users/{id}; el id llega por la ruta.</summary>
public sealed record UpdateUserHttpRequest(
    string? DisplayName,
    IReadOnlyCollection<string>? Roles,
    string? Email = null,
    PhoneNumberHttpRequest? Phone = null)
{
    public override string ToString() => nameof(UpdateUserHttpRequest);
}
