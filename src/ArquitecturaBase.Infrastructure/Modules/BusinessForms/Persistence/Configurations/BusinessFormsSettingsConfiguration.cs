using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Configurations;

/// <summary>
/// Configuración › Formularios (RC4-A): a table of one row. The primary key guarantees it from the model and the CHECK against an INSERT made
/// by hand. The ranges live in the domain and the validator, so widening one does not need a migration.
/// </summary>
internal sealed class BusinessFormsSettingsConfiguration : IEntityTypeConfiguration<BusinessFormsSettings>
{
    public void Configure(EntityTypeBuilder<BusinessFormsSettings> builder)
    {
        builder.ToTable("BusinessFormsSettings", table => table.HasCheckConstraint("CK_BusinessFormsSettings_SingleRow",
            "\"Id\" = '" + BusinessFormsSettings.SingletonIdValue + "'"));
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Revision).IsConcurrencyToken();
    }
}
