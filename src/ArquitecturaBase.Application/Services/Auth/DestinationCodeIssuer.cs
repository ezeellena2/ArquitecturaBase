using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// El pedido de un código con que una cuenta prueba, desde el perfil, que un correo es suyo (sección 12 del spec del
/// ingreso con WhatsApp): emite el código con <see cref="LoginCodeIssuer"/>, que sigue siendo el único que emite, y se
/// lo manda a ese correo. Lo verifica <see cref="DestinationCodeVerifier"/>, igual que el de un número, que pide el
/// módulo del canal (con WhatsApp, su módulo). Corre dentro del límite del punto de entrada, con OnSuccess: no guarda, y
/// lo que encola queda marcado en la fila antes del commit.
/// </summary>
internal sealed class DestinationCodeIssuer(
    LoginCodeIssuer issuer,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue)
{
    /// <summary>El pedido por correo de <paramref name="user"/>, ya validado.</summary>
    public async Task<Result<RequestEmailCodeResponse>> RequestEmailCodeAsync(
        UserAccount user, RequestEmailCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);

        var emailResult = Email.Create(request.Email);
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

        // Se encola antes del commit para que el código se confirme ya marcado como enviado. Si la cola está llena, el
        // código se guarda como no enviado.
        if (emailQueue.TryEnqueue(
            templateRenderer.RenderEmailVerificationCode(
                email.Value, issued.Value.Code, issued.Value.LifetimeMinutes, CultureInfo.GetCultureInfo(UserCultures.Of(user)))))
        {
            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        return new RequestEmailCodeResponse(issued.Value.ResendCooldownSeconds);
    }
}
