using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence;

/// <summary>
/// Contexto de EF Core: Identity (usuarios y roles con Id Guid), las tablas propias y las claves de Data Protection.
/// Las entidades de OpenIddict llegan por las opciones que arma AddInfrastructure. Toma de este ensamblado un
/// IEntityTypeConfiguration por entidad.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<LoginCode> LoginCodes => Set<LoginCode>();

    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();

    public DbSet<LoginLink> LoginLinks => Set<LoginLink>();

    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();

    public DbSet<WhatsAppContact> WhatsAppContacts => Set<WhatsAppContact>();

    public DbSet<WhatsAppMessage> WhatsAppMessages => Set<WhatsAppMessage>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        builder.ApplySoftDeleteQueryFilter();
    }
}
