namespace ArquitecturaBase.Application.Models.Users;

/// <summary>El perfil propio: el usuario sale de la sesión, no del cuerpo del pedido.</summary>
public sealed record UpdateProfileRequest(string? DisplayName, string? Culture, string? TimeZoneId);
