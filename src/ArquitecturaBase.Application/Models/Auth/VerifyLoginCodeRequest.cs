namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>El código enviado por correo o WhatsApp; se informa exactamente uno de los dos destinos.</summary>
public sealed record VerifyLoginCodeRequest(string? Email, string? Code, string? ReturnUrl, string? Phone = null)
{
    internal bool IsByPhone => !string.IsNullOrWhiteSpace(Phone);

    // MVC registra los argumentos de la acción con ToString(): nunca exponer el código ni el número completo.
    public override string ToString() => nameof(VerifyLoginCodeRequest);
}
