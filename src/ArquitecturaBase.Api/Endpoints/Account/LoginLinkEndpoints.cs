using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth.RedeemLoginLink;

namespace ArquitecturaBase.Api.Endpoints.Account;

/// <summary>
/// Canje del enlace que manda el bot al chat (sección 11 del spec del ingreso con WhatsApp). La vista previa ya vive
/// en LoginLinkController. Esta ruta mantiene JSON, la política compartida de verificación y el registro permanente,
/// incluso cuando WhatsApp está apagado.
/// </summary>
internal sealed class LoginLinkEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/login-link").AllowAnonymous().WithTags("Account");

        // 204 con la cookie de la sesión; después el SPA hace el OIDC de siempre.
        group.MapPost("/redeem", async (
                RedeemLoginLinkCommand command,
                ICommandHandler<RedeemLoginLinkCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequireRateLimiting(RateLimitingExtensions.LoginVerifyPolicy);
    }
}
