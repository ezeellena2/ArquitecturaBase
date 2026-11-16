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
        builder.Property(code => code.RequestedFromAddress).HasMaxLength(LoginCode.MaxRequestedFromAddressLength);
        builder.Ignore(code => code.AttemptsLeft);

        // Todas las búsquedas son por destino, del código más nuevo al más viejo. El propósito no va en el índice:
        // los límites buscan por destino solo, y un destino tiene pocas filas por las que filtrar.
        builder.HasIndex(code => new { code.Destination, code.CreatedAtUtc });

        // N7: el tope diario por dirección de los códigos por correo cuenta los de una dirección en las últimas 24 horas,
        // en cada pedido de un código por correo. Una dirección tiene pocas filas por día (el tope), y sin el índice cada
        // pedido recorrería la tabla. Los códigos sin dirección conocida no se buscan por ella.
        builder.HasIndex(code => new { code.RequestedFromAddress, code.CreatedAtUtc })
            .HasFilter("\"RequestedFromAddress\" IS NOT NULL");

        // N31: el tope diario de todos, el de WhatsApp y el del correo (N7), cuenta los envíos de un canal en las últimas
        // 24 horas en cada pedido de un código. Sin el índice recorrería la tabla entera; solo entran los que salieron.
        builder.HasIndex(code => new { code.Channel, code.SentAtUtc })
            .HasFilter("\"SentAtUtc\" IS NOT NULL");

        // N31: y los de una cuenta, los que pidió desde su perfil en un canal. Los códigos de ingreso no tienen cuenta y no
        // entran.
        builder.HasIndex(code => new { code.RequestedByUserId, code.Channel, code.SentAtUtc })
            .HasFilter("\"RequestedByUserId\" IS NOT NULL");
    }
}
