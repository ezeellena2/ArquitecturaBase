using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Application.Features.Users.ConfirmEmail;
using ArquitecturaBase.Application.Features.Users.ConfirmPhoneLink;
using ArquitecturaBase.Application.Features.Users.GetCurrentUser;
using ArquitecturaBase.Application.Features.Users.RequestEmailCode;
using ArquitecturaBase.Application.Features.Users.RequestPhoneLinkCode;
using ArquitecturaBase.Application.Features.Users.UnlinkOwnPhone;
using ArquitecturaBase.Application.Features.Users.UpdateProfile;

namespace ArquitecturaBase.Api.Endpoints.Users;

/// <summary>
/// El propio usuario: perfil, roles, permisos, idioma, zona horaria y último ingreso (sección 5.6), la edición de su
/// perfil (sección 9 del spec de la Fase 4) y sus medios de ingreso: vincular y desvincular su WhatsApp y agregar un
/// correo (sección 12 del spec del ingreso con WhatsApp). No pide permisos: alcanza con el bearer, y cada uno maneja lo
/// suyo.
/// </summary>
internal sealed class MeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").RequireAuthorization().WithTags("Users");

        group.MapGet("", async (
                IQueryHandler<GetCurrentUserQuery, CurrentUserResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(new GetCurrentUserQuery(), cancellationToken)).ToHttpResult());

        group.MapPut("", async (
                UpdateProfileCommand command,
                ICommandHandler<UpdateProfileCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult());

        MapWhatsApp(app, group);
        MapEmail(group);
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

    private static void MapEmail(RouteGroupBuilder group)
    {
        // Como el del número: 202 con la misma forma sea el correo libre o de otra cuenta.
        group.MapPost("/email/code", async (
                RequestEmailCodeCommand command,
                ICommandHandler<RequestEmailCodeCommand, RequestEmailCodeResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(command, cancellationToken);

                return result.IsSuccess ? TypedResults.Accepted((string?)null, result.Value) : result.Error.ToProblem();
            })
            .RequireRateLimiting(RateLimitingExtensions.LoginCodePolicy);

        group.MapPut("/email", async (
                ConfirmEmailCommand command,
                ICommandHandler<ConfirmEmailCommand> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequireRateLimiting(RateLimitingExtensions.LoginVerifyPolicy);
    }
}
