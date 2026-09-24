using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Features.WhatsApp;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Users.ConfirmPhoneLink;

/// <summary>
/// Vincula el número a la propia cuenta con el código que llegó por WhatsApp (sección 12 del spec del ingreso con
/// WhatsApp). Recién con el código correcto se dice si el número ya es de otra cuenta, activa o borrada: para tenerlo
/// hay que ser dueño del número. En ese caso el código queda gastado igual. Si la cuenta ya tenía otro número, el nuevo
/// lo reemplaza, verificado, y para el anterior es como desvincularlo: su contacto de WhatsApp se suelta y los enlaces
/// de ingreso que el bot ya mandó a ese chat dejan de servir (<see cref="PhoneNumberChange"/>). El contacto del número
/// nuevo, si alguien ya le escribió al bot desde ahí, queda vinculado a la cuenta.
/// </summary>
internal sealed class ConfirmPhoneLinkCommandHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    DestinationCodeVerifier verifier,
    WhatsAppContactLinker contactLinker,
    PhoneNumberChange phoneChange)
    : ICommandHandler<ConfirmPhoneLinkCommand>
{
    public async Task<Result> Handle(ConfirmPhoneLinkCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (currentUser.UserId is not { } userId)
        {
            return UserErrors.NotFound;
        }

        var phoneResult = PhoneNumber.Create(command.Phone);

        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;
        var verification = await verifier.VerifyAsync(LoginCodeDestination.ForPhone(phone), userId, command.Code!, cancellationToken);

        if (verification.IsFailure)
        {
            return verification.Error;
        }

        // Primero el contacto y después la cuenta, como el bot. Si el bot le está contestando al chat del número anterior,
        // se lo espera, y el enlace que mande se invalida acá.
        await phoneChange.LockAsync(userId, phone, cancellationToken);

        // La cuenta se lee recién ahora, con los locks: mientras se esperaba al bot, el bot pudo haberla escrito (verifica
        // el número del chat si estaba sin verificar), y con lo leído antes el guardado chocaría con su ConcurrencyStamp.
        var user = await identityService.FindByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // Una cuenta borrada conserva su número y el índice único lo sigue reservando (sección 6.1 del spec).
        var owner = await identityService.FindByPhoneAsync(phone, cancellationToken);

        if (owner is null ? await identityService.IsDeletedPhoneAsync(phone, cancellationToken) : owner.Id != user.Id)
        {
            return UserErrors.PhoneAlreadyExists;
        }

        try
        {
            await identityService.SetPhoneAsync(user.Id, phone, confirmed: true, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Otra cuenta se quedó con el número entre la búsqueda y el guardado: por ejemplo, el bot le creó una cuenta
            // en ese momento. Con el lock del destino no pasa entre dos que confirman, pero sí con lo que no lo toma.
            return UserErrors.PhoneAlreadyExists;
        }

        // Después del número, y no antes: si el guardado del número fallara, ni el vínculo tiene que quedar pendiente ni
        // los enlaces del chat del número que la cuenta conserva tienen que dejar de servir.
        if (user.PhoneNumber is { } previous && previous != phone.Value)
        {
            await phoneChange.VoidPendingLinksAsync(user.Id, cancellationToken);
        }

        await contactLinker.LinkNumberAsync(phone, user.Id, cancellationToken);

        return Result.Success();
    }
}
