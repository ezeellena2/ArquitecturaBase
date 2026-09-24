using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.UnlinkOwnPhone;

/// <summary>Desvincular el propio WhatsApp. La cuenta sale del token: no hay nada más que mandar ni que validar.</summary>
public sealed record UnlinkOwnPhoneCommand : ICommand;
