namespace ArquitecturaBase.Application.Models.Users;

/// <summary>
/// Desvincular el número propio desde el perfil. Si la cuenta tiene un correo verificado, lleva el código que se le
/// mandó a ese correo para confirmarlo (N11); si no, va sin código.
/// </summary>
public sealed record UnlinkOwnPhoneRequest(string? Code);
