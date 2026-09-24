using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>Vinculación y desvinculación del WhatsApp propio; conserva locks, cuotas y consumo de códigos.</summary>
internal sealed class ProfileWhatsAppOperations(
    ICurrentUser currentUser,
    IUserReader users,
    IUserRepository userRepository,
    LoginCodeIssuer issuer,
    DestinationCodeVerifier verifier,
    IPhoneNumberParser phoneNumbers,
    IWhatsAppAvailability whatsApp,
    IWhatsAppOutbox outbox,
    IOptions<WhatsAppLoginOptions> whatsAppOptions,
    IOptions<LoginCodeOptions> codeOptions,
    WhatsAppContactLinker contactLinker,
    PhoneNumberChange phoneChange,
    UserGuards guards,
    ServiceRequestValidator<RequestPhoneLinkCodeRequest> requestValidator,
    ServiceRequestValidator<ConfirmPhoneLinkRequest> confirmValidator,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<RequestPhoneLinkCodeResponse>> RequestCodeAsync(
        RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken)
    {
        if (await requestValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        // La acción no existe con WhatsApp apagado. Este guard impide emitir un código sin entrega si se invoca
        // directamente el servicio desde otro consumidor.
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no code to link a number can be requested.");
        }

        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var phoneResult = phoneNumbers.Parse(request.Country, request.Number);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;
        if (!whatsAppOptions.Value.AllowsCountry(phoneNumbers.RegionOf(phone)))
        {
            return WhatsAppErrors.CountryNotSupported;
        }

        // El emisor comparte límites por destino y cuota diaria con los códigos de ingreso.
        var issued = await issuer.IssueVerificationCodeAsync(
            LoginCodeDestination.ForPhone(phone), user.Id, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        // Si la cola no lo toma, queda sin fecha de envío y no consume la cuota.
        if (outbox.TryEnqueue(new WhatsAppLoginCodeMessage(phone, UserCultures.Of(user), issued.Value.Code)))
        {
            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RequestPhoneLinkCodeResponse(
            codeOptions.Value.ResendCooldownSeconds, phone.Value, phoneNumbers.Mask(phone));
    }

    public async Task<Result> ConfirmAsync(ConfirmPhoneLinkRequest request, CancellationToken cancellationToken)
    {
        if (await confirmValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        var result = await ConfirmValidatedAsync(request, cancellationToken);

        // La verificación puede contar un intento o
        // consumir el código aun cuando el número está ocupado o la escritura choca con el índice único.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<Result> ConfirmValidatedAsync(
        ConfirmPhoneLinkRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return UserErrors.NotFound;
        }

        var phoneResult = PhoneNumber.Create(request.Phone);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;
        var verification = await verifier.VerifyAsync(
            LoginCodeDestination.ForPhone(phone), userId, request.Code!, cancellationToken);
        if (verification.IsFailure)
        {
            return verification.Error;
        }

        // Mismo orden que el bot: contacto, enlaces y recién entonces lectura/escritura de la cuenta.
        await phoneChange.LockAsync(userId, phone, cancellationToken);
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var owner = await users.FindByPhoneAsync(phone, cancellationToken);
        if (owner is null ? await users.IsDeletedPhoneAsync(phone, cancellationToken) : owner.Id != user.Id)
        {
            return UserErrors.PhoneAlreadyExists;
        }

        try
        {
            await userRepository.SetPhoneAsync(user.Id, phone, confirmed: true, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return UserErrors.PhoneAlreadyExists;
        }

        if (user.PhoneNumber is { } previous && previous != phone.Value)
        {
            await phoneChange.VoidPendingLinksAsync(user.Id, cancellationToken);
        }

        await contactLinker.LinkNumberAsync(phone, user.Id, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> UnlinkAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return UserErrors.NotFound;
        }

        await phoneChange.LockAsync(userId, newPhone: null, cancellationToken);
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // La persona conserva la sesión actual; solo un administrador revoca sesiones al quitar un número.
        if (user.PhoneNumber is not null)
        {
            if (!await guards.HasOtherLoginMethodAsync(user, cancellationToken))
            {
                return UserErrors.LastLoginMethod;
            }

            await userRepository.RemovePhoneAsync(user.Id, cancellationToken);
        }

        await contactLinker.UnlinkUserAsync(user.Id, cancellationToken);
        await phoneChange.VoidPendingLinksAsync(user.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
