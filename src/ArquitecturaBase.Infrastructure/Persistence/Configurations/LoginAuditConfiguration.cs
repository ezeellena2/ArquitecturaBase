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
        builder.HasIndex(audit => audit.UserId);
    }
}
