using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// El correo de <c>Seed:AdminEmail</c>. Compara con el valor normalizado de <see cref="Email"/>, así que mayúsculas y
/// espacios en la configuración no cambian nada. Vacío: nadie es el administrador inicial.
/// </summary>
internal sealed class InitialAdmin(IOptions<SeedOptions> seedOptions) : IInitialAdmin
{
    public bool IsInitialAdmin(Email email)
    {
        ArgumentNullException.ThrowIfNull(email);

        var adminEmail = Email.Create(seedOptions.Value.AdminEmail);

        return adminEmail.IsSuccess && adminEmail.Value.Equals(email);
    }
}
