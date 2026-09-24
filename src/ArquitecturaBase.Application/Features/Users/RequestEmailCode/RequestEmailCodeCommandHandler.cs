using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Users.RequestEmailCode;

/// <summary>
/// Pide el código para agregar un correo a la propia cuenta (sección 12 del spec del ingreso con WhatsApp): el correo
/// del código, con textos que dicen para qué es, en el idioma de la cuenta. Como con el número, la respuesta, el código
/// y el envío son los mismos sea el correo libre o de otra cuenta, activa o borrada: "ya existe una cuenta con ese
/// correo" se dice recién al confirmar con el código correcto. Los límites por destino son los del ingreso y se
/// comparten con él. El correo se encola antes de guardar, como el del ingreso.
/// </summary>
internal sealed class RequestEmailCodeCommandHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    LoginCodeIssuer issuer,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    IOptions<LoginCodeOptions> options)
    : ICommandHandler<RequestEmailCodeCommand, RequestEmailCodeResponse>
{
    public async Task<Result<RequestEmailCodeResponse>> Handle(RequestEmailCodeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = currentUser.UserId is { } userId
            ? await identityService.FindByIdAsync(userId, cancellationToken)
            : null;

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var emailResult = Email.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var issued = await issuer.IssueVerificationCodeAsync(LoginCodeDestination.ForEmail(email), user.Id, cancellationToken);

        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var settings = options.Value;

        await emailQueue.EnqueueAsync(
            templateRenderer.RenderEmailVerificationCode(
                email.Value, issued.Value.Code, settings.LifetimeMinutes, CultureInfo.GetCultureInfo(UserCultures.Of(user))),
            cancellationToken);

        issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);

        return new RequestEmailCodeResponse(settings.ResendCooldownSeconds);
    }
}
