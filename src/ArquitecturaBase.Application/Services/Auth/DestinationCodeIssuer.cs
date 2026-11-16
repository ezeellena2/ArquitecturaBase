using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// El pedido de un código con que una cuenta prueba, desde el perfil, que un correo es suyo (sección 12 del spec del
/// ingreso con WhatsApp), o con que confirma con su correo verificado un cambio del número: emite el código con <see cref="LoginCodeIssuer"/>, que sigue siendo el único que emite, y se
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

        return await SendAsync(
            user, emailResult.Value, LoginCodePurpose.VerifyDestination, templateRenderer.RenderEmailVerificationCode, cancellationToken);
    }

    /// <summary>
    /// El código que confirma, con el correo verificado de <paramref name="user"/> (que quien llama ya leyó), un cambio del
    /// número desde el perfil (cada medio confirma los cambios del otro, 2026-11-16). Es de
    /// <see cref="LoginCodePurpose.ConfirmChange"/>: no sirve para verificar un correo, ni el de verificar para confirmar
    /// un cambio. Comparte los límites del destino, y el texto dice qué cambio confirma.
    /// </summary>
    public Task<Result<RequestEmailCodeResponse>> RequestConfirmationCodeAsync(
        UserAccount user, Email email, ProfileChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(email);

        Func<string, string, int, CultureInfo, EmailMessage> render = change switch
        {
            ProfileChange.LinkPhone => templateRenderer.RenderPhoneLinkCode,
            ProfileChange.UnlinkPhone => templateRenderer.RenderPhoneUnlinkCode,
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown profile change."),
        };

        return SendAsync(user, email, LoginCodePurpose.ConfirmChange, render, cancellationToken);
    }

    private async Task<Result<RequestEmailCodeResponse>> SendAsync(
        UserAccount user,
        Email email,
        LoginCodePurpose purpose,
        Func<string, string, int, CultureInfo, EmailMessage> render,
        CancellationToken cancellationToken)
    {
        var destination = LoginCodeDestination.ForEmail(email);
        var issued = purpose is LoginCodePurpose.ConfirmChange
            ? await issuer.IssueConfirmationCodeAsync(destination, user.Id, cancellationToken)
            : await issuer.IssueVerificationCodeAsync(destination, user.Id, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        // Se encola antes del commit para que el código se confirme ya marcado como enviado. Si la cola está llena, el
        // código se guarda como no enviado.
        if (emailQueue.TryEnqueue(
            render(email.Value, issued.Value.Code, issued.Value.LifetimeMinutes, CultureInfo.GetCultureInfo(UserCultures.Of(user)))
                with { ExpiresAtUtc = issued.Value.LoginCode.ExpiresAtUtc }))
        {
            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        return new RequestEmailCodeResponse(issued.Value.ResendCooldownSeconds);
    }
}
