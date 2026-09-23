using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;

/// <summary>
/// El número como lo escribió la persona, con o sin 0, 15 o 9, y el país elegido (ISO 3166-1 alfa-2), que solo se usa
/// para leer un número que no empieza con "+".
/// </summary>
public sealed record RequestWhatsAppLoginCodeCommand(string? Country, string? Number) : ICommand<RequestWhatsAppLoginCodeResponse>;
