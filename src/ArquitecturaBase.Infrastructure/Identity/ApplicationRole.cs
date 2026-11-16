using ArquitecturaBase.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// Rol de Identity. Sus permisos son role claims de tipo "permission" (sección 5.6). Es auditable: quién cambió
/// los permisos de un rol importa tanto como quién cambió la configuración.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>, IAuditable
{
    public const int DescriptionMaxLength = 256;

    public ApplicationRole()
    {
        Id = Guid.CreateVersion7();
    }

    public ApplicationRole(string name)
        : this()
    {
        Name = name;
    }

    public string? Description { get; set; }

    /// <summary>
    /// La revisión del nombre, la descripción y los permisos (N2), que la avanza <c>RoleRepository</c> cuando alguno
    /// cambia. Nace en 1, como la de la configuración. No es el <c>ConcurrencyStamp</c>, que Identity cambia con cada
    /// guardado y arma la clave del caché de permisos.
    /// </summary>
    public long Revision { get; private set; } = 1;

    public void AdvanceRevision() => Revision++;

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }
}
