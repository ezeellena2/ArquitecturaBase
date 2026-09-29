using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
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
    /// hecho con lo leído antes chocaría con el ConcurrencyStamp de Identity, que también es un 500.
    /// </summary>
    public async Task LockAsync(Guid userId, PhoneNumber? newPhone, CancellationToken cancellationToken)
    {
        foreach (var participant in _participants)
        {
            await participant.LockAsync(userId, newPhone, cancellationToken);
        }

        await loginLinks.LockAccountAsync(userId, cancellationToken);
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
    /// Corre dentro del límite del perfil con OnAnyResult: la verificación puede contar un intento o gastar el código
    /// aun cuando el número está ocupado o la escritura choca con el índice único.
    /// </summary>
    public async Task<Result> ConfirmOwnPhoneAsync(
        Guid userId, PhoneNumber phone, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phone);

        var verification = await verifier.VerifyAsync(
            LoginCodeDestination.ForPhone(phone), userId, code, cancellationToken);
        if (verification.IsFailure)
        {
            return verification.Error;
        }

        // Mismo orden que el bot: participantes, enlaces y recién entonces lectura/escritura de la cuenta.
        await LockAsync(userId, phone, cancellationToken);
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
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
