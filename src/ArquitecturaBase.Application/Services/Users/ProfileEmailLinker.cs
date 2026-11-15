using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Le pone a la cuenta el correo que confirmó desde el perfil: verifica el código con
/// <see cref="DestinationCodeVerifier"/>, que lo gasta, y guarda el correo, ya verificado, si está libre. La cuenta de
/// <c>Seed:AdminEmail</c> no cambia de correo (N25): el arranque la busca por ese correo para devolverle Admin, y con
/// otro la perdería; se rechaza antes de verificar, así el código no se gasta. No guarda por su cuenta: corre adentro
/// del límite de <see cref="ProfileService"/>, que confirma con lo que haya pasado.
/// </summary>
internal sealed class ProfileEmailLinker(
    IUserReader users, IUserRepository userRepository, DestinationCodeVerifier verifier, IInitialAdmin initialAdmin)
{
    public async Task<Result> LinkAsync(UserAccount user, Email email, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(email);

        if (!string.Equals(user.Email, email.Value, StringComparison.OrdinalIgnoreCase)
            && user.Email is not null
            && Email.Create(user.Email) is { IsSuccess: true } current
            && initialAdmin.IsInitialAdmin(current.Value))
        {
            return UserErrors.InitialAdmin;
        }

        var verification = await verifier.VerifyAsync(
            LoginCodeDestination.ForEmail(email), user.Id, code, cancellationToken);
        if (verification.IsFailure)
        {
            return verification.Error;
        }

        // El índice único conserva también los correos de cuentas borradas.
        var owner = await users.FindByEmailAsync(email, cancellationToken);
        if (owner is null ? await users.ExistsDeletedByEmailAsync(email, cancellationToken) : owner.Id != user.Id)
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
