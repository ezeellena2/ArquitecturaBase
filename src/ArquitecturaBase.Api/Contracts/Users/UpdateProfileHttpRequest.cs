namespace ArquitecturaBase.Api.Contracts.Users;

/// <summary>El cuerpo de PUT /api/me: el usuario sale de la sesión, no del cuerpo del pedido.</summary>
public sealed record UpdateProfileHttpRequest(string? DisplayName, string? Culture, string? TimeZoneId)
{
    // MVC registra los argumentos de la acción con ToString(): no exponer datos del perfil en logs.
    public override string ToString() => nameof(UpdateProfileHttpRequest);
}
