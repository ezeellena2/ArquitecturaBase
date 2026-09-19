using Microsoft.AspNetCore.Identity;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>Rol de Identity. Sus permisos son role claims de tipo "permission" (sección 5.6).</summary>
public sealed class ApplicationRole : IdentityRole<Guid>
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
}
