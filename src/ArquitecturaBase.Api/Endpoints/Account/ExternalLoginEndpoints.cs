using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Features.Auth.SignInWithExternalProvider;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;

namespace ArquitecturaBase.Api.Endpoints.Account;

/// <summary>Ingreso con Google (sección 5.4). Son navegaciones del navegador: los errores vuelven al login del SPA.</summary>
internal sealed class ExternalLoginEndpoints : IEndpoint
{
    public const string CallbackPath = "/account/external/callback";

    // La clave que usa SignInManager.ConfigureExternalAuthenticationProperties. Ese método no se puede llamar desde la
    // Api (es genérico en ApplicationUser, de Infrastructure); GetExternalLoginInfoAsync lee de acá el proveedor.
    public const string LoginProviderKey = "LoginProvider";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/account/external").AllowAnonymous().WithTags("Account");

        group.MapGet("/google", async (string? returnUrl, IAuthenticationSchemeProvider schemes) =>
        {
            // Sin ClientId configurado, Google no se registra.
            if (await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is null)
            {
                return Results.NotFound();
            }

            if (!ReturnUrls.IsAuthorizeRequest(returnUrl))
            {
                return new ValidationError(new Dictionary<string, string[]>
                {
                    ["returnUrl"] = [ValidationMessages.ReturnUrlInvalid],
                }).ToProblem();
            }

            var properties = new AuthenticationProperties { RedirectUri = CallbackPath + QueryString.Create("returnUrl", returnUrl) };
            properties.Items[LoginProviderKey] = GoogleDefaults.AuthenticationScheme;

            return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
        });

        group.MapGet("/callback", async (
            string? returnUrl,
            ICommandHandler<SignInWithExternalProviderCommand, SignInWithExternalProviderResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.Handle(new SignInWithExternalProviderCommand(returnUrl), cancellationToken);

            return result.IsSuccess
                ? Results.LocalRedirect(result.Value.ReturnUrl)
                : Results.Redirect(ReturnUrls.LoginPath + QueryString.Create("error", result.Error.Code));
        });
    }
}
