using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;
using ArquitecturaBase.Application.Interfaces.Integrations;

namespace ArquitecturaBase.Api.Endpoints.Account;

/// <summary>
/// Pedido condicional de código por WhatsApp. El correo y la verificación usan AccountController.
/// Solo acepta JSON y no hay CORS: un formulario de otro sitio no puede iniciar una sesión (CSRF de login).
/// </summary>
internal sealed class LoginCodeEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/login-code").AllowAnonymous().WithTags("Account");

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
    }
}
