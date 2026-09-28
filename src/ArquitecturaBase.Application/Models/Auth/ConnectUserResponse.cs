using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Models.Auth;

/// <summary>Datos vigentes de la cuenta para emitir tokens y responder userinfo.</summary>
public sealed record ConnectUserResponse(UserAccount Account, IReadOnlyCollection<string> Roles);
