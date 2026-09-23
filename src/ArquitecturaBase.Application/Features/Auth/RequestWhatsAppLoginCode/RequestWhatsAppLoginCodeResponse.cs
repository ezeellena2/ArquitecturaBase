namespace ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;

/// <summary>
/// Siempre la misma forma, exista o no la cuenta, como el pedido por correo. <see cref="Phone"/> es el número ya
/// interpretado, en formato internacional: es el que el SPA manda después al verify. <see cref="MaskedPhone"/> es para
/// mostrar a dónde se mandó el código.
/// </summary>
public sealed record RequestWhatsAppLoginCodeResponse(int ResendAfterSeconds, string Phone, string MaskedPhone);
