namespace ArquitecturaBase.Application.Features.Users.RequestPhoneLinkCode;

/// <summary>
/// Siempre la misma forma, sea el número libre o de otra cuenta. <see cref="Phone"/> es el número ya interpretado, en
/// formato internacional: es el que el SPA manda después al confirmar. <see cref="MaskedPhone"/> es para mostrar a
/// dónde se mandó el código.
/// </summary>
public sealed record RequestPhoneLinkCodeResponse(int ResendAfterSeconds, string Phone, string MaskedPhone);
