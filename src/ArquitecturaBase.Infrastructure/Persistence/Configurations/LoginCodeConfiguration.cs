using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

/// <summary>
/// El canal y el propósito se guardan como texto, como el método de ingreso en LoginAudits: se leen de una consulta
/// SQL sin tener que traducir un número.
/// </summary>
internal sealed class LoginCodeConfiguration : IEntityTypeConfiguration<LoginCode>
{
    public const int CodeHashMaxLength = 128;
    public const int ChannelMaxLength = 20;
    public const int PurposeMaxLength = 20;

    public void Configure(EntityTypeBuilder<LoginCode> builder)
    {
        // Un correo es lo más largo que puede ser un destino: un número entra con lugar de sobra.
        builder.Property(code => code.Destination).HasMaxLength(Email.MaxLength);
        builder.Property(code => code.Channel).HasConversion<string>().HasMaxLength(ChannelMaxLength);
        builder.Property(code => code.Purpose).HasConversion<string>().HasMaxLength(PurposeMaxLength);
        builder.Property(code => code.CodeHash).HasMaxLength(CodeHashMaxLength);
        builder.Ignore(code => code.AttemptsLeft);

        // Todas las búsquedas son por destino, del código más nuevo al más viejo. El propósito no va en el índice:
        // los límites buscan por destino solo, y un destino tiene pocas filas por las que filtrar.
        builder.HasIndex(code => new { code.Destination, code.CreatedAtUtc });
    }
}
