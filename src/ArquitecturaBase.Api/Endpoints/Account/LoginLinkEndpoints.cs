using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth.PreviewLoginLink;
using ArquitecturaBase.Application.Features.Auth.RedeemLoginLink;

namespace ArquitecturaBase.Api.Endpoints.Account;

/// <summary>
/// El enlace que manda el bot al chat (sección 11 del spec del ingreso con WhatsApp). Como el resto de /account, solo
/// aceptan JSON y no hay CORS: un formulario de otro sitio no puede canjear un enlace e iniciar una sesión (CSRF de
/// login). Los dos usan la política de límites por IP del verify del código, y cuentan junto con él (sección 13): son
/// la otra forma de probar suerte con algo que abre una sesión. Se mapean siempre, esté WhatsApp prendido o no: sin el
/// bot no hay enlaces, y cualquier token responde que no sirve.
/// </summary>
internal sealed class LoginLinkEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/login-link").AllowAnonymous().WithTags("Account");

        // A qué cuenta lleva el enlace. No lo consume, así que la vista previa del chat no lo gasta.
        group.MapPost("/preview", async (
                PreviewLoginLinkQuery query,
                IQueryHandler<PreviewLoginLinkQuery, LoginLinkPreviewResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(query, cancellationToken)).ToHttpResult())
            .RequireRateLimiting(RateLimitingExtensions.LoginVerifyPolicy);

        // 204 con la cookie de la sesión; después el SPA hace el OIDC de siempre.
        group.MapPost("/redeem", async (
                RedeemLoginLinkCommand command,
                ICommandHandler<RedeemLoginLinkCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequireRateLimiting(RateLimitingExtensions.LoginVerifyPolicy);
    }
}
