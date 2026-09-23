using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.RedeemLoginLink;

/// <summary>
/// Canjea el enlace del chat y abre la sesión del servidor (sección 11 del spec del ingreso con WhatsApp). Guarda
/// aunque falle: el enlace consumido y la auditoría tienen que quedar.
/// </summary>
public sealed record RedeemLoginLinkCommand(string? Token) : ICommand, IPersistChangesOnFailure
{
    /// <summary>Un record imprime sus propiedades, y el token sirve para entrar: no tiene que terminar en un log.</summary>
    public override string ToString() => nameof(RedeemLoginLinkCommand);
}
