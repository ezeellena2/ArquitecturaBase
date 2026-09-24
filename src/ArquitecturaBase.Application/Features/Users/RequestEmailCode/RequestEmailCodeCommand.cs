using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Users.RequestEmailCode;

/// <summary>El correo que la persona quiere agregar a su cuenta. La cuenta sale del token, no del cuerpo.</summary>
public sealed record RequestEmailCodeCommand(string? Email) : ICommand<RequestEmailCodeResponse>;
