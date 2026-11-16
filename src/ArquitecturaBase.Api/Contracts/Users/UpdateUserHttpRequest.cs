namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>
/// El cuerpo de PUT /api/users/{id}; el id llega por la ruta. <c>ExpectedRevision</c> es la <c>revision</c> del detalle que
/// se editó: si la cuenta cambió en el medio, responde 409 <c>Users.User.RevisionConflict</c> (N2).
/// </summary>
public sealed record UpdateUserHttpRequest(
    string? DisplayName,
    IReadOnlyCollection<string>? Roles,
    long ExpectedRevision,
    string? Email = null,
    PhoneNumberHttpRequest? Phone = null)
{
    public override string ToString() => nameof(UpdateUserHttpRequest);
}
