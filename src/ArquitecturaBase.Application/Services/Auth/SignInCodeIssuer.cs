using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// El pedido de un código para entrar por correo, ya validado: emite el código con <see cref="LoginCodeIssuer"/> y se lo
/// manda a la cuenta o a quien todavía puede crear una. Corre dentro del límite de <see cref="LoginCodeService"/>: no
/// guarda, y lo que encola queda marcado en la fila antes del commit. El pedido por un número lo da el módulo del canal
/// (con WhatsApp, su módulo), con el mismo emisor.
/// </summary>
internal sealed class SignInCodeIssuer(
    LoginCodeIssuer issuer,
    IUserReader users,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    AccountCreationPolicy accountCreation)
{
    // El pedido por correo, ya validado: corre dentro del límite de LoginCodeService.RequestLoginCodeAsync.
    public async Task<Result<RequestLoginCodeResponse>> RequestLoginCodeCoreAsync(
        RequestLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var issued = await issuer.IssueSignInCodeAsync(LoginCodeDestination.ForEmail(email), cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var user = await users.FindByEmailAsync(email, cancellationToken);

        // La fila también se guarda para un correo desconocido en InviteOnly: los límites no pueden revelar
        // si existe la cuenta. El administrador inicial puede recibir el email antes de crear su cuenta.
        if (user is not null || await accountCreation.AllowsNewAccountAsync(email, cancellationToken))
        {
            var culture = user is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(user.Culture);

            // Se encola antes del commit para que la fila se confirme ya marcada como enviada. Encolar no espera al
            // SMTP, así que no alarga el lock del destino. Si la cola está llena, el código se guarda como no enviado.
            if (emailQueue.TryEnqueue(
                templateRenderer.RenderLoginCode(email.Value, issued.Value.Code, issued.Value.LifetimeMinutes, culture)))
            {
                issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
            }
        }

        return new RequestLoginCodeResponse(issued.Value.ResendCooldownSeconds);
    }
}
