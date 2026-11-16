using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

internal sealed class LoginAuditConfiguration : IEntityTypeConfiguration<LoginAudit>
{
    public const int MethodMaxLength = 20;
    public const int FailureReasonMaxLength = 100;
    public const int IpAddressMaxLength = 45;

    public void Configure(EntityTypeBuilder<LoginAudit> builder)
    {
        // El correo o el número: el correo es el más largo de los dos.
        builder.Property(audit => audit.Identifier).HasMaxLength(Email.MaxLength);
        builder.Property(audit => audit.Method).HasConversion<string>().HasMaxLength(MethodMaxLength);
        builder.Property(audit => audit.FailureReason).HasMaxLength(FailureReasonMaxLength);
        builder.Property(audit => audit.IpAddress).HasMaxLength(IpAddressMaxLength);
        builder.Property(audit => audit.UserAgent).HasMaxLength(LoginAudit.MaxUserAgentLength);

        builder.HasIndex(audit => audit.OccurredAtUtc);
        // El último ingreso de la sesión (GET /api/me) busca por persona y fecha. Sólo con la fecha, recorre los ingresos
        // de todos desde el último de esa persona.
        builder.HasIndex(audit => new { audit.UserId, audit.OccurredAtUtc });

        // N16: la retención busca los ingresos viejos que todavía guardan la IP, el navegador o, los fallidos, lo que la
        // persona escribió. Las filas que ya limpió salen del índice, así cada corrida no recorre todos los años anteriores.
        // El filtro es el mismo que el de IdentityRetentionRepository: si no, PostgreSQL no puede usar el índice.
        builder.HasIndex(audit => audit.OccurredAtUtc, "IX_LoginAudits_OccurredAtUtc_RequestInfo")
            .HasFilter("\"IpAddress\" IS NOT NULL OR \"UserAgent\" IS NOT NULL OR (NOT \"Succeeded\" AND \"Identifier\" IS NOT NULL)");
    }
}
