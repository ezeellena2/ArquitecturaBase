using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.RequestPhoneLinkCode;

/// <summary>
/// El número que la persona quiere vincular a su cuenta, tal como lo escribió, y el país elegido: la misma forma que el
/// pedido del código para entrar con WhatsApp. La cuenta sale del token, no del cuerpo.
/// </summary>
public sealed record RequestPhoneLinkCodeCommand(string? Country, string? Number) : ICommand<RequestPhoneLinkCodeResponse>;
