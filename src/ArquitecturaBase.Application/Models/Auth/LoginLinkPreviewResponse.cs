namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>
/// Lo que muestra "Vas a entrar como". <see cref="DisplayName"/> es el nombre de la cuenta o, si no tiene, su correo;
/// null si no tiene ninguno de los dos. El número va siempre enmascarado, y es null si la cuenta no tiene número.
/// </summary>
public sealed record LoginLinkPreviewResponse(string? DisplayName, string? MaskedPhone);
