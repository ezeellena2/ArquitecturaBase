namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// El perfil propio: el usuario sale de la sesión, no del cuerpo del pedido. <see cref="ExpectedRevision"/> es la de la
/// cuenta, como en la administración: el perfil y el administrador no se pisan el nombre (N2).
/// </summary>
public sealed record UpdateProfileRequest(string? DisplayName, string? Culture, string? TimeZoneId, long ExpectedRevision);
