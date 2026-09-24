using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Auth.PreviewLoginLink;

/// <summary>
/// Mostrarle el nombre a quien tiene el enlace no agrega riesgo: con el enlace ya podría entrar (sección 11 del spec del
/// ingreso con WhatsApp). Lo que no dice es el estado de la cuenta: con una deshabilitada o bloqueada responde igual
/// que con una activa, y eso se informa recién al canjear (sección 6.4).
/// </summary>
internal sealed class PreviewLoginLinkQueryHandler(
    ILoginLinkRepository loginLinks,
    ISecureTokenGenerator tokens,
    IIdentityService identityService,
    IPhoneNumberParser phoneNumbers,
    TimeProvider timeProvider)
    : IQueryHandler<PreviewLoginLinkQuery, LoginLinkPreviewResponse>
{
    public async Task<Result<LoginLinkPreviewResponse>> Handle(PreviewLoginLinkQuery query, CancellationToken cancellationToken)
    {
        var loginLink = await loginLinks.GetByTokenHashAsync(tokens.Hash(query.Token!), cancellationToken);

        if (loginLink is null || !loginLink.IsActive(timeProvider.GetUtcNow().UtcDateTime))
        {
            return LoginLinkErrors.Invalid;
        }

        // Una cuenta borrada después de emitir el enlace ya no existe para nadie: el enlace es uno más que no sirve.
        var user = await identityService.FindByIdAsync(loginLink.UserId, cancellationToken);

        if (user is null)
        {
            return LoginLinkErrors.Invalid;
        }

        return new LoginLinkPreviewResponse(user.DisplayName ?? user.Email, MaskedPhoneOf(user));
    }

    private string? MaskedPhoneOf(UserAccount user)
    {
        var phone = PhoneNumber.Create(user.PhoneNumber);

        return phone.IsSuccess ? phoneNumbers.Mask(phone.Value) : null;
    }
}
