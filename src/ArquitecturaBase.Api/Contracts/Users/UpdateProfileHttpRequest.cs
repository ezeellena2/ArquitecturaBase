namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>
/// El cuerpo de PUT /api/me: el usuario sale de la sesión, no del cuerpo del pedido. <c>ExpectedRevision</c> es la
/// <c>revision</c> de GET /api/me: si la cuenta cambió en el medio, responde 409 <c>Users.User.RevisionConflict</c> (N2).
/// </summary>
public sealed record UpdateProfileHttpRequest(string? DisplayName, string? Culture, string? TimeZoneId, long ExpectedRevision)
{
    // MVC registra los argumentos de la acción con ToString(): no exponer datos del perfil en logs.
    public override string ToString() => nameof(UpdateProfileHttpRequest);
}
