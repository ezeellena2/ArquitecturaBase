using System.Text.Json;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Configurations.Connection;

/// <summary>Un lado del robot por conexión (ADR 0026), sin secretos; desconectar lo borra en cascada.</summary>
internal sealed class ArcaRobotRunConfiguration : IEntityTypeConfiguration<ArcaRobotRun>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ArcaRobotRun> builder)
    {
        builder.ToTable("ArcaRobotRuns");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ConnectionId, x.Side }).IsUnique();
        builder.HasOne<ArcaConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(x => x.Side).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Failure).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.CompletedSteps).HasColumnType("text[]");
        builder.Property(x => x.CurrentStep).HasMaxLength(120);
        builder.Property(x => x.FailedStep).HasMaxLength(120);
        builder.Property(x => x.AlertKey).HasMaxLength(160);
        builder.Property(x => x.Outputs).HasColumnType("jsonb").HasConversion(
            value => JsonSerializer.Serialize(value, JsonOptions),
            value => new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(value, JsonOptions)!, StringComparer.Ordinal),
            new ValueComparer<Dictionary<string, string>>(
                (left, right) => JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions),
                value => JsonSerializer.Serialize(value, JsonOptions).GetHashCode(StringComparison.Ordinal),
                value => new Dictionary<string, string>(value, StringComparer.Ordinal)));
    }
}
