using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth.RequestLoginCode;
using ArquitecturaBase.Application.Features.Auth.VerifyLoginCode;

namespace ArquitecturaBase.Api.Endpoints.Account;

/// <summary>
/// Ingreso con código (sección 5.2). Solo aceptan JSON y no hay CORS: un formulario de otro sitio no puede iniciar
/// una sesión (CSRF de login).
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

        group.MapPost("/verify", async (
                VerifyLoginCodeCommand command,
                ICommandHandler<VerifyLoginCodeCommand, VerifyLoginCodeResponse> handler,
                CancellationToken cancellationToken) =>
            (await handler.Handle(command, cancellationToken)).ToHttpResult())
            .RequireRateLimiting(RateLimitingExtensions.LoginVerifyPolicy);
    }
}
