using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;

internal sealed class AiAssistantToolExecutionConfiguration : IEntityTypeConfiguration<AiAssistantToolExecution>
{
    public void Configure(EntityTypeBuilder<AiAssistantToolExecution> builder)
    {
        builder.ToTable("AiAssistantToolExecutions");
        builder.HasKey(tool => tool.Id);
        builder.Property(tool => tool.ToolCallId).HasMaxLength(200);
        builder.Property(tool => tool.ToolId).HasMaxLength(100);
        builder.Property(tool => tool.EvidenceId).HasMaxLength(100);
        builder.Property(tool => tool.State).HasMaxLength(20);
        AiConversationConfiguration.ConfigurePermissions(builder.Property(tool => tool.RequiredPermissions));
        builder.HasIndex(tool => new { tool.TurnId, tool.Ordinal }).IsUnique();
        builder.HasOne<AiAssistantTurn>().WithMany().HasForeignKey(tool => tool.TurnId).OnDelete(DeleteBehavior.Cascade);
    }
}
