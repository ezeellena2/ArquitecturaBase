using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Features.Users.ConfirmPhoneLink;
using ArquitecturaBase.Application.Features.Users.RequestPhoneLinkCode;
using ArquitecturaBase.Application.Features.Users.UnlinkOwnPhone;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>
/// Las rutas heredadas de WhatsApp del perfil propio. La lectura, edición y vinculación de correo usan MeController.
/// No pide permisos: alcanza con el bearer, y cada uno maneja lo suyo.
/// </summary>
internal sealed class MeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").RequireAuthorization().WithTags("Users");

        MapWhatsApp(app, group);
    }

    /// <summary>
    /// Los pedidos de código usan los mismos límites por IP que el ingreso, y las confirmaciones los del verify: cuentan
    /// junto con ellos. Con WhatsApp apagado, pedir el código para vincular no existe, igual que el del ingreso; confirmar
    /// y desvincular sí, porque no mandan nada.
    /// </summary>
    private static void MapWhatsApp(IEndpointRouteBuilder app, RouteGroupBuilder group)
    {
        if (app.ServiceProvider.GetRequiredService<IWhatsAppAvailability>().IsEnabled)
        {
            // 202 con la misma forma sea el número libre o de otra cuenta: la respuesta no revela nada.
            group.MapPost("/whatsapp/code", async (
                    RequestPhoneLinkCodeCommand command,
                    ICommandHandler<RequestPhoneLinkCodeCommand, RequestPhoneLinkCodeResponse> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.Handle(command, cancellationToken);

                    return result.IsSuccess ? TypedResults.Accepted((string?)null, result.Value) : result.Error.ToProblem();
                })
                .RequireRateLimiting(RateLimitingExtensions.LoginCodePolicy);
        }

        group.MapPut("/whatsapp", async (
                ConfirmPhoneLinkCommand command,
                ICommandHandler<ConfirmPhoneLinkCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequireRateLimiting(RateLimitingExtensions.LoginVerifyPolicy);

        group.MapDelete("/whatsapp", async (
                ICommandHandler<UnlinkOwnPhoneCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new UnlinkOwnPhoneCommand(), cancellationToken)).ToHttpResult());
    }
}
