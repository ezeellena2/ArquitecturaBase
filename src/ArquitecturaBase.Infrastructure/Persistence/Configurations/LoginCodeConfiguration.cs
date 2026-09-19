using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

internal sealed class LoginCodeConfiguration : IEntityTypeConfiguration<LoginCode>
{
    public const int CodeHashMaxLength = 128;

    public void Configure(EntityTypeBuilder<LoginCode> builder)
    {
        builder.Property(code => code.Email).HasMaxLength(Email.MaxLength);
        builder.Property(code => code.CodeHash).HasMaxLength(CodeHashMaxLength);
        builder.Ignore(code => code.AttemptsLeft);

        // Todas las búsquedas son por email, del código más nuevo al más viejo.
        builder.HasIndex(code => new { code.Email, code.CreatedAtUtc });
    }
}
