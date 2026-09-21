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

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }
}
