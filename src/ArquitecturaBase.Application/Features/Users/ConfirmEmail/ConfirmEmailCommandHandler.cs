using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Users.ConfirmEmail;

/// <summary>
/// Agrega el correo a la propia cuenta con el código que llegó a esa dirección (sección 12 del spec del ingreso con
/// WhatsApp). Recién con el código correcto se dice si el correo ya es de otra cuenta, activa o borrada, y en ese caso
/// el código queda gastado igual. Si la cuenta ya tenía otro correo (por ejemplo, uno que cargó un administrador y
/// nunca se verificó), el nuevo lo reemplaza, verificado.
/// </summary>
internal sealed class ConfirmEmailCommandHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    DestinationCodeVerifier verifier)
    : ICommandHandler<ConfirmEmailCommand>
{
    public async Task<Result> Handle(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = currentUser.UserId is { } userId
            ? await identityService.FindByIdAsync(userId, cancellationToken)
            : null;

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var emailResult = Email.Create(command.Email);

        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var verification = await verifier.VerifyAsync(LoginCodeDestination.ForEmail(email), user.Id, command.Code!, cancellationToken);

        if (verification.IsFailure)
        {
            return verification.Error;
        }

        // Una cuenta borrada conserva su correo y el índice único lo sigue reservando.
        var owner = await identityService.FindByEmailAsync(email, cancellationToken);

        if (owner is null ? await identityService.IsDeletedEmailAsync(email, cancellationToken) : owner.Id != user.Id)
        {
            return UserErrors.AlreadyExists;
        }

        try
        {
            await identityService.SetEmailAsync(user.Id, email, confirmed: true, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Otra cuenta se quedó con el correo entre la búsqueda y el guardado (por ejemplo, alguien entró con Google
            // con esa dirección en ese momento).
            return UserErrors.AlreadyExists;
        }

        return Result.Success();
    }
}
