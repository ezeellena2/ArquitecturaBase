using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

/// <summary>
/// El tope diario del correo (N7) para los tests que emiten códigos o invitaciones: los valores por defecto, que esos tests
/// no alcanzan, sobre los mismos repositorios en memoria que usan. Sus topes los prueba EmailQuotaGuardTests.
/// </summary>
internal static class EmailQuotaGuards
{
    public static EmailQuotaGuard For(
        ILoginCodeRepository codes,
        TimeProvider clock,
        IUserInvitationRepository? invitations = null,
        EmailQuotaOptions? options = null) =>
        new(
            codes,
            invitations ?? new InMemoryUserInvitationRepository(),
            new FakeRequestInfo(),
            Options.Create(options ?? new EmailQuotaOptions()),
            new FakeEmailQuotaWarnings(),
            clock,
            NullLogger<EmailQuotaGuard>.Instance);
}
