using ArquitecturaBase.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Persistence.Configurations;

/// <summary>
/// Las invitaciones se buscan por la cuenta, de la más nueva a la más vieja: la última para el detalle y la última que
/// salió para la espera entre una y otra. El canal se guarda como texto, como el método de ingreso en LoginAudits. Como
/// en los códigos y los enlaces, la cuenta no lleva clave foránea: las cuentas no se borran de verdad. El id de Meta no
/// lleva índice: se busca el mensaje por él, no la invitación.
/// </summary>
internal sealed class UserInvitationConfiguration : IEntityTypeConfiguration<UserInvitation>
{
    public const int ChannelMaxLength = 20;

    public void Configure(EntityTypeBuilder<UserInvitation> builder)
    {
        builder.Property(invitation => invitation.Channel).HasConversion<string>().HasMaxLength(ChannelMaxLength);
        builder.Property(invitation => invitation.WaMessageId).HasMaxLength(UserInvitation.MaxWaMessageIdLength);

        builder.HasIndex(invitation => new { invitation.UserId, invitation.SentAtUtc });
    }
}
