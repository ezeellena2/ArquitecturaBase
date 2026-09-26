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

/// <summary>Los dos casos de correo del perfil comparten usuario y códigos, pero no cambian la sesión actual.</summary>
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
    ServiceRequestValidator<ConfirmEmailRequest> confirmValidator,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<RequestEmailCodeResponse>> RequestCodeAsync(
        RequestEmailCodeRequest request, CancellationToken cancellationToken)
    {
        var validationError = await requestValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

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

        var settings = options.Value;
        await emailQueue.EnqueueAsync(
            templateRenderer.RenderEmailVerificationCode(
                email.Value, issued.Value.Code, settings.LifetimeMinutes, CultureInfo.GetCultureInfo(UserCultures.Of(user))),
            cancellationToken);
        issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);

        // Se encola antes de guardar para que el código se confirme ya marcado como enviado (con la cola llena,
        // EmailQueue descarta el correo y el código igual queda marcado). El guardado también suelta el lock del
        // destino que tomó el emisor.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RequestEmailCodeResponse(settings.ResendCooldownSeconds);
    }

    public async Task<Result> ConfirmAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var validationError = await confirmValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            return validationError;
        }

        var result = await ConfirmValidatedAsync(request, cancellationToken);

        // VerifyAsync puede consumir el código o contar un intento.
        // Se confirma también cuando la cuenta no existe, el correo está ocupado o la escritura choca con el índice.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<Result> ConfirmValidatedAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
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
            return UserErrors.AlreadyExists;
        }

        return Result.Success();
    }
}
