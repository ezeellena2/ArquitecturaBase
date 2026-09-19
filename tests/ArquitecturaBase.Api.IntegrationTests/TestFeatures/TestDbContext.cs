using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

/// <summary>El ApplicationDbContext de producción más la tabla de Widgets.</summary>
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : ApplicationDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(widget =>
        {
            widget.ToTable("widgets");
            widget.Property(w => w.Name).HasMaxLength(Widget.NameMaxLength);
        });

        // Al final: la base aplica sus convenciones (por ejemplo, el filtro de soft delete) a todas las entidades.
        base.OnModelCreating(modelBuilder);
    }
}
