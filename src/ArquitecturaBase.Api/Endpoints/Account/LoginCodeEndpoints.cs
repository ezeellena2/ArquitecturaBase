using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Features.Auth.RequestLoginCode;
using ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;
using ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

namespace ArquitecturaBase.Api.Endpoints.Account;

/// <summary>
/// Ingreso con código, por correo o por WhatsApp (sección 5.2 y sección 10 del spec del ingreso con WhatsApp). Solo
/// aceptan JSON y no hay CORS: un formulario de otro sitio no puede iniciar una sesión (CSRF de login).
/// </summary>
internal sealed class LoginCodeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/login-code").AllowAnonymous().WithTags("Account");

        // 202 exista o no la cuenta: la respuesta no revela nada.
        group.MapPost("", async (
                RequestLoginCodeCommand command,
                ICommandHandler<RequestLoginCodeCommand, RequestLoginCodeResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(command, cancellationToken);

                return result.IsSuccess ? TypedResults.Accepted((string?)null, result.Value) : result.Error.ToProblem();
            })
            .RequireRateLimiting(RateLimitingExtensions.LoginCodePolicy);

        // Con WhatsApp apagado la ruta no existe: el 404 lo arma el framework, igual que el de cualquier ruta
        // desconocida (Http.NotFound), sin importar el cuerpo. Que esté prendido se decide una vez, al arrancar.
        if (app.ServiceProvider.GetRequiredService<IWhatsAppAvailability>().IsEnabled)
        {
            // Como el pedido por correo: 202 con la misma forma exista o no la cuenta, y la misma política de límites
            // por IP, que cuenta los pedidos de los dos canales juntos.
            group.MapPost("/whatsapp", async (
                    RequestWhatsAppLoginCodeCommand command,
                    ICommandHandler<RequestWhatsAppLoginCodeCommand, RequestWhatsAppLoginCodeResponse> handler,
                    CancellationToken cancellationToken) =>
                {
                    var result = await handler.Handle(command, cancellationToken);

                    return result.IsSuccess ? TypedResults.Accepted((string?)null, result.Value) : result.Error.ToProblem();
                })
                .RequireRateLimiting(RateLimitingExtensions.LoginCodePolicy);
        }

        group.MapPost("/verify", async (
                VerifyLoginCodeCommand command,
                ICommandHandler<VerifyLoginCodeCommand, VerifyLoginCodeResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequireRateLimiting(RateLimitingExtensions.LoginVerifyPolicy);
    }
}
