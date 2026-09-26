using ArquitecturaBase.Application.Configuration.Auth;
using System.Globalization;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Los dos casos de correo del perfil. No abren ni confirman transacciones: trabajan dentro del límite de ProfileService,
/// que valida afuera con Validate*Async y elige la política de cada uno. No cambian la sesión actual.
/// </summary>
internal sealed class ProfileEmailOperations(
    ICurrentUser currentUser,
    IUserReader users,
    IUserRepository userRepository,
    LoginCodeIssuer issuer,
    DestinationCodeVerifier verifier,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    IOptions<LoginCodeOptions> options,
    ServiceRequestValidator<RequestEmailCodeRequest> requestValidator,
    ServiceRequestValidator<ConfirmEmailRequest> confirmValidator)
{
    public Task<ValidationError?> ValidateRequestAsync(RequestEmailCodeRequest request, CancellationToken cancellationToken) =>
        requestValidator.ValidateAsync(request, cancellationToken);

    /// <summary>El pedido de código, ya validado. Corre dentro del límite de ProfileService, con OnSuccess.</summary>
    public async Task<Result<RequestEmailCodeResponse>> RequestCodeAsync(
        RequestEmailCodeRequest request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

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

        // Se encola antes del commit para que el código se confirme ya marcado como enviado (con la cola llena,
        // EmailQueue descarta el correo y el código igual queda marcado).
        var settings = options.Value;
        await emailQueue.EnqueueAsync(
            templateRenderer.RenderEmailVerificationCode(
                email.Value, issued.Value.Code, settings.LifetimeMinutes, CultureInfo.GetCultureInfo(UserCultures.Of(user))),
            cancellationToken);
        issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);

        return new RequestEmailCodeResponse(settings.ResendCooldownSeconds);
    }

    public Task<ValidationError?> ValidateConfirmAsync(ConfirmEmailRequest request, CancellationToken cancellationToken) =>
        confirmValidator.ValidateAsync(request, cancellationToken);

    /// <summary>
    /// La confirmación, ya validada. Corre dentro del límite de ProfileService con OnAnyResult: la verificación puede
    /// contar un intento o gastar el código aunque la cuenta no exista, el correo esté ocupado o la escritura choque con
    /// el índice único.
    /// </summary>
    public async Task<Result> ConfirmAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var verification = await verifier.VerifyAsync(
            LoginCodeDestination.ForEmail(email), user.Id, request.Code!, cancellationToken);
        if (verification.IsFailure)
        {
            return verification.Error;
        }

        // El índice único conserva también los correos de cuentas borradas.
        var owner = await users.FindByEmailAsync(email, cancellationToken);
        if (owner is null ? await users.IsDeletedEmailAsync(email, cancellationToken) : owner.Id != user.Id)
        {
            return UserErrors.AlreadyExists;
        }

        try
        {
            await userRepository.SetEmailAsync(user.Id, email, confirmed: true, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // El savepoint deshizo solo ese guardado: el código sigue gastado y se confirma igual.
            return UserErrors.AlreadyExists;
        }

        return Result.Success();
    }
}
