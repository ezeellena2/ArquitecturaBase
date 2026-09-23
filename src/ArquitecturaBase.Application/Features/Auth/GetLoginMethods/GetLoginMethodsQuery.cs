using ArquitecturaBase.Application.Abstractions.Messaging;

namespace ArquitecturaBase.Application.Features.Auth.GetLoginMethods;

/// <summary>Qué medios de ingreso ofrece la pantalla de login, además del correo, que está siempre.</summary>
public sealed record GetLoginMethodsQuery : IQuery<LoginMethodsResponse>;
