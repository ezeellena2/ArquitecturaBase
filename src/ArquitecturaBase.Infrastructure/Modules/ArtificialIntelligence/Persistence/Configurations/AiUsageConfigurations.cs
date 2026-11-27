using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Configurations;

internal sealed class AiUsageMonthConfiguration : IEntityTypeConfiguration<AiUsageMonth>
{
    public void Configure(EntityTypeBuilder<AiUsageMonth> builder)
    {
        builder.ToTable("AiUsageMonths", table =>
        {
            table.HasCheckConstraint("CK_AiUsageMonths_FirstDay", "EXTRACT(DAY FROM \"Month\") = 1");
            table.HasCheckConstraint("CK_AiUsageMonths_Amounts", "\"Requests\" >= 0 AND \"UnknownUsageRequests\" >= 0 AND \"UnknownUsageRequests\" <= \"Requests\"");
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Month, x.Provider, x.Model }).IsUnique();
        builder.Property(x => x.Provider).HasConversion(new AiProviderConverter()).HasMaxLength(20);
        builder.Property(x => x.Model).HasMaxLength(AiModelPrice.MaximumModelLength);
        builder.Property(x => x.AudioSeconds).HasColumnType("numeric");
    }
}

internal sealed class AiModelPriceConfiguration : IEntityTypeConfiguration<AiModelPrice>
{
    public void Configure(EntityTypeBuilder<AiModelPrice> builder)
    {
        builder.ToTable("AiModelPrices");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Provider, x.Model }).IsUnique();
        builder.Property(x => x.Provider).HasConversion(new AiProviderConverter()).HasMaxLength(20);
        builder.Property(x => x.Model).HasMaxLength(AiModelPrice.MaximumModelLength);
        builder.Property(x => x.InputUsdPerMillion).HasPrecision(18, AiModelPrice.MaximumDecimals);
        builder.Property(x => x.OutputUsdPerMillion).HasPrecision(18, AiModelPrice.MaximumDecimals);
        builder.Property(x => x.AudioUsdPerMinute).HasPrecision(18, AiModelPrice.MaximumDecimals);
        builder.Property(x => x.Revision).IsConcurrencyToken();
    }
}
