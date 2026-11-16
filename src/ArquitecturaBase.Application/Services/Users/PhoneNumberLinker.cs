using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Lo que comparten los cambios del número de una cuenta (sección 12 del spec del ingreso con WhatsApp): vincularlo,
/// reemplazarlo por otro y desvincularlo. Los locks, la anulación de enlaces, quitar el número y soltarlo los comparten el
/// perfil y la administración (editar y desvincular); confirmar el número propio es solo del perfil. Toma los locks en el
/// orden del bot y, cuando la cuenta suelta un número, invalida los enlaces de ingreso que el bot ya mandó a ese chat.
/// Para el número que se va, reemplazarlo es lo mismo que desvincularlo: su chat puede no ser más de esta persona (el
/// caso del teléfono robado), y los enlaces van atados a la cuenta, no al número. Las sesiones no se tocan: eso lo
/// decide quien llama. No sabe qué ata un módulo al número (con WhatsApp, el contacto del chat): se lo avisa a los
/// participantes (<see cref="IPhoneLinkParticipant"/>), en el orden en que se registraron. Sin ninguno, el número es solo
/// un dato de la cuenta. No abre ni confirma transacciones: trabaja dentro del límite de quien llama.
/// </summary>
internal sealed class PhoneNumberLinker(
    IUserReader users,
    IUserRepository userRepository,
    DestinationCodeVerifier verifier,
    IEnumerable<IPhoneLinkParticipant> participants,
    ILoginLinkRepository loginLinks,
    TimeProvider timeProvider)
{
    /// <summary>El campo del pedido con el código que llegó al correo de la cuenta.</summary>
    public const string EmailCodeField = "emailCode";

    private readonly IPhoneLinkParticipant[] _participants = [.. participants];

    /// <summary>
    /// Toma los locks antes de escribir nada de la cuenta, en el mismo orden que el bot: primero los de lo que los
    /// participantes atan al número (con WhatsApp, las filas de los contactos: el de la cuenta y, si pasa a
    /// <paramref name="newPhone"/>, los de ese número) y después el de los enlaces de la cuenta, con el que un canje en
    /// curso termina antes de que se invaliden. El bot toma el contacto y después la cuenta: al revés, cada uno espera al
    /// otro, Postgres corta a uno con un deadlock (40P01) y, si le toca al perfil, sale un 500. En el mismo orden, el que
    /// llega segundo espera al primero. Las excepciones las resuelve el módulo (con WhatsApp, el bot: el chat que contesta
    /// puede no estar entre esas filas, así que con el lock de la cuenta vuelve a mirar si el número sigue siendo de ella,
    /// y el contacto que suelta para vincular el suyo no lo espera). Corre dentro de la transacción del caso de uso, que
    /// los locks exigen, así el número, lo que atan los participantes y los enlaces se guardan juntos. Quien llama lee la
    /// cuenta después, no antes: mientras espera, el bot puede escribirla (verifica el número del chat), y un guardado
    /// hecho con lo leído antes chocaría con el ConcurrencyStamp de Identity, que también es un 500. Termina con la fila de
    /// la cuenta (<see cref="IUserRepository.LockAsync"/>), donde la toman la edición y el perfil.
    /// </summary>
    public async Task LockAsync(Guid userId, PhoneNumber? newPhone, CancellationToken cancellationToken)
    {
        foreach (var participant in _participants)
        {
            await participant.LockAsync(userId, newPhone, cancellationToken);
        }

        await loginLinks.LockAccountAsync(userId, cancellationToken);

        // Y la fila de la cuenta, que una edición de la administración o el perfil toman en el mismo lugar del orden (N2).
        await userRepository.LockAsync(userId, cancellationToken);
    }

    /// <summary>
    /// La cuenta soltó un número: invalida los enlaces de ingreso que todavía sirven, que el bot pudo haber mandado al
    /// chat de ese número. Va después de guardar el número: si el guardado fallara, la cuenta conserva el que tenía, y
    /// los enlaces de su chat siguen sirviendo.
    /// </summary>
    public async Task VoidPendingLinksAsync(Guid userId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var link in await loginLinks.ListActiveAsync(userId, nowUtc, cancellationToken))
        {
            link.Invalidate(nowUtc);
        }
    }

    /// <summary>
    /// <paramref name="userId"/> confirma desde el perfil que <paramref name="phone"/> es suyo, con el código que pidió.
    /// Cada medio confirma los cambios del otro (2026-11-16): un número verificado no se reemplaza desde el perfil
    /// (<see cref="UserErrors.PhoneAlreadySet"/>; para cambiarlo se desvincula y se vincula el nuevo), y vincular uno, o
    /// reemplazar uno sin verificar que cargó un administrador, en una cuenta con correo verificado pide también
    /// <paramref name="emailCode"/>, el código que confirma el cambio mandado a ese correo (error atado a
    /// <see cref="EmailCodeField"/>). Volver a confirmar el mismo número verificado no cambia nada y no lo pide. Lo que
    /// no se puede hacer se rechaza antes de revisar ningún código; los dos se revisan antes de gastar alguno, en el orden
    /// único de los locks (correo, número, participantes y cuenta). Corre dentro del límite del perfil con OnAnyResult:
    /// la verificación puede contar un intento o gastar los códigos aun cuando el número está ocupado o la escritura
    /// choca con el índice único.
    /// </summary>
    public async Task<Result> ConfirmOwnPhoneAsync(
        Guid userId, PhoneNumber phone, string code, string? emailCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        // Esta lectura sólo decide qué se rechaza antes de los códigos y qué correo pide uno; la que vale para escribir es
        // la de después de los locks.
        var before = await users.FindByIdAsync(userId, cancellationToken);
        if (before is null)
        {
            return UserErrors.NotFound;
        }

        if (ReplacesVerifiedPhone(before, phone))
        {
            return UserErrors.PhoneAlreadySet;
        }

        var email = before.PhoneNumberConfirmed ? null : ProfileService.VerifiedEmailOf(before);
        if (email is not null && string.IsNullOrEmpty(emailCode))
        {
            return FieldErrors.On(UserErrors.ConfirmationCodeRequired, EmailCodeField);
        }

        CheckedCode? emailCheck = null;
        if (email is not null)
        {
            var check = await verifier.CheckAsync(
                LoginCodeDestination.ForEmail(email), LoginCodePurpose.ConfirmChange, userId, emailCode!, cancellationToken);
            if (check.IsFailure)
            {
                return FieldErrors.OnWithMetadata(check.Error, EmailCodeField);
            }

            emailCheck = check.Value;
        }

        var phoneCheck = await verifier.CheckAsync(
            LoginCodeDestination.ForPhone(phone), LoginCodePurpose.VerifyDestination, userId, code, cancellationToken);
        if (phoneCheck.IsFailure)
        {
            return phoneCheck.Error;
        }

        // Los dos estaban bien: se gastan juntos, aunque después el número resulte de otra cuenta.
        DestinationCodeVerifier.Consume(phoneCheck.Value);
        if (emailCheck is not null)
        {
            DestinationCodeVerifier.Consume(emailCheck);
        }

        // Mismo orden que el bot: participantes, enlaces y recién entonces lectura/escritura de la cuenta.
        await LockAsync(userId, phone, cancellationToken);
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // Mientras esperaba, otro pedido pudo verificar un número o el correo (el ingreso con código lo hace): la regla
        // vale con lo de ahora.
        if (ReplacesVerifiedPhone(user, phone))
        {
            return UserErrors.PhoneAlreadySet;
        }

        if (emailCheck is null && !user.PhoneNumberConfirmed && ProfileService.VerifiedEmailOf(user) is not null)
        {
            return FieldErrors.On(UserErrors.ConfirmationCodeRequired, EmailCodeField);
        }

        var owner = await users.FindByPhoneAsync(phone, cancellationToken);
        if (owner is null ? await users.ExistsDeletedByPhoneAsync(phone, cancellationToken) : owner.Id != user.Id)
        {
            return UserErrors.PhoneAlreadyExists;
        }

        try
        {
            await userRepository.SetPhoneAsync(user.Id, phone, confirmed: true, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // El savepoint deshizo solo ese guardado: el código sigue gastado y se confirma igual.
            return UserErrors.PhoneAlreadyExists;
        }

        if (user.PhoneNumber is { } previous && previous != phone.Value)
        {
            await VoidPendingLinksAsync(user.Id, cancellationToken);
        }

        // Con el número ya guardado: el participante ata lo suyo al número nuevo (con WhatsApp, el contacto del chat).
        foreach (var participant in _participants)
        {
            await participant.PhoneConfirmedAsync(user.Id, phone, cancellationToken);
        }

        return Result.Success();
    }

    private static bool ReplacesVerifiedPhone(UserAccount user, PhoneNumber phone) =>
        user is { PhoneNumberConfirmed: true, PhoneNumber: { } current } && current != phone.Value;

    /// <summary>
    /// Quita el número de la cuenta. Quien llama ya tomó los locks (<see cref="LockAsync"/>), leyó la cuenta y decidió
    /// que puede: después suelta el número (<see cref="ReleasePhoneAsync"/>) y los enlaces o las sesiones.
    /// </summary>
    public Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken) =>
        userRepository.RemovePhoneAsync(userId, cancellationToken);

    /// <summary>
    /// La cuenta soltó su número (lo quitó, o un administrador lo reemplazó por uno sin confirmar): cada participante
    /// suelta lo que tenía atado (con WhatsApp, el contacto de la cuenta, si tiene uno). Quien llama ya tomó los locks
    /// (<see cref="LockAsync"/>). El reemplazo desde el perfil no pasa por acá: lo cubre el aviso de
    /// <see cref="ConfirmOwnPhoneAsync"/>.
    /// </summary>
    public async Task ReleasePhoneAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (var participant in _participants)
        {
            await participant.PhoneReleasedAsync(userId, cancellationToken);
        }
    }
}
