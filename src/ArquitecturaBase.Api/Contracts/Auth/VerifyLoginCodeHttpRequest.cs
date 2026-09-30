namespace ArquitecturaBase.Api.Contracts.Auth;

/// <summary>
/// El cuerpo de POST /account/login-code/verify: el código enviado por correo o WhatsApp, con exactamente uno de los
/// dos destinos.
/// </summary>
public sealed record VerifyLoginCodeHttpRequest(string? Email, string? Code, string? ReturnUrl, string? Phone = null,
    bool? Register = null, string? DisplayName = null)
{
    // MVC registra los argumentos de la acción con ToString(): nunca exponer el código ni el número completo.
    public override string ToString() => nameof(VerifyLoginCodeHttpRequest);
}
