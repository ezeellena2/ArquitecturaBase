using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

/// <summary>
/// El código que llegó por correo o por WhatsApp. Lleva <see cref="Email"/> o <see cref="Phone"/>, exactamente uno
/// (sección 10 del spec del ingreso con WhatsApp). <see cref="Phone"/> va en formato internacional: es el que devolvió
/// el pedido del código, no lo que tipeó la persona.
/// </summary>
public sealed record VerifyLoginCodeCommand(string? Email, string? Code, string? ReturnUrl, string? Phone = null)
    : ICommand<VerifyLoginCodeResponse>, IPersistChangesOnFailure
{
    /// <summary>Si se presenta con el número. Lo miran el validador y el caso de uso, con la misma regla.</summary>
    internal bool IsByPhone => !string.IsNullOrWhiteSpace(Phone);
}
