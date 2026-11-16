namespace ArquitecturaBase.Api.Modules.WhatsApp.Contracts;

/// <summary>
/// El cuerpo de PUT /api/me/whatsapp: el número internacional y el código recibido por WhatsApp y, si la cuenta tiene un
/// correo verificado, el código que confirma el cambio, pedido con <c>LinkPhone</c> a
/// <c>POST /api/me/email/confirmation-code</c>.
/// </summary>
public sealed record ConfirmPhoneLinkHttpRequest(string? Phone, string? Code, string? EmailCode = null)
{
    // MVC registra los argumentos de la acción con ToString(): nunca revelar el número ni los códigos.
    public override string ToString() => nameof(ConfirmPhoneLinkHttpRequest);
}
