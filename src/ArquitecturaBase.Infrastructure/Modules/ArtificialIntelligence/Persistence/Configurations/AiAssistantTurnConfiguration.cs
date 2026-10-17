using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;

internal sealed class AiAssistantTurnConfiguration : IEntityTypeConfiguration<AiAssistantTurn>
{
    public void Configure(EntityTypeBuilder<AiAssistantTurn> builder)
    {
        builder.ToTable("AiAssistantTurns", table => table.HasCheckConstraint("CK_AiAssistantTurns_Ordinal", "\"Ordinal\" > 0"));
        builder.HasKey(turn => turn.Id);
        builder.Property(turn => turn.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(turn => turn.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(turn => turn.Stage).HasMaxLength(20);
        builder.Property(turn => turn.Culture).HasMaxLength(10);
        builder.Property(turn => turn.TimeZoneId).HasMaxLength(100);
        builder.Property(turn => turn.Model).HasMaxLength(150);
        builder.Property(turn => turn.ExecutionProfileJson).HasMaxLength(AiAssistantTurn.MaximumExecutionProfileBytes);
        builder.Property(turn => turn.FailureCode).HasMaxLength(100);
        builder.Property(turn => turn.Revision).IsConcurrencyToken();
        builder.Property(turn => turn.Fence).IsConcurrencyToken();
        builder.HasIndex(turn => new { turn.ConversationId, turn.Ordinal }).IsUnique();
        builder.HasIndex(turn => turn.ConversationId).IsUnique().HasFilter("\"State\" IN ('Pending', 'Running')");
        builder.HasIndex(turn => turn.OwnerUserId).IsUnique().HasFilter("\"State\" IN ('Pending', 'Running')");
        builder.HasIndex(turn => new { turn.State, turn.SubmittedAtUtc, turn.Id });
        builder.HasIndex(turn => new { turn.State, turn.DeadlineAtUtc });
        builder.HasIndex(turn => turn.ContentExpiresAtUtc);
        builder.HasOne(turn => turn.Conversation).WithMany()
            .HasForeignKey(turn => new { turn.ConversationId, turn.OwnerUserId })
            .HasPrincipalKey(conversation => new { conversation.Id, conversation.OwnerUserId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(turn => turn.Conversation.State == AiConversationState.Active);
    }
}
