using System.Text.RegularExpressions;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

/// <summary>The guide for removing Forms lists the tables its migration drops; they must be the ones the module maps.</summary>
public sealed partial class FormRemovalGuideTests
{
    [Fact]
    public void The_guide_lists_exactly_the_tables_the_module_maps()
    {
        var guide = File.ReadAllText(Path.Combine(SolutionRoot.FullPath, "docs", "guides", "quitar-formularios.md"));
        var block = TablesBlock().Match(guide);
        Assert.True(block.Success, "The guide needs the list of tables between the table markers.");
        var listed = block.Groups[1].Value.Split([',', ' ', '\r', '\n', '`'], StringSplitOptions.RemoveEmptyEntries).Order().ToList();
        Assert.Equal(listed.Count, listed.Distinct().Count());
        Assert.Equal(ModuleTables().Order(), listed);
        Assert.Contains($"{listed.Count} tablas", guide);
    }

    private static List<string> ModuleTables()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseNpgsql();
        options.UseOpenIddict<Guid>();
        using var db = new ApplicationDbContext(options.Options);
        return db.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(entity => entity.ClrType.Namespace?.StartsWith("ArquitecturaBase.Domain.Modules.BusinessForms", StringComparison.Ordinal) == true)
            .Select(entity => entity.GetTableName()!).Distinct().ToList();
    }

    [GeneratedRegex(@"<!-- tablas -->(.*?)<!-- /tablas -->", RegexOptions.Singleline)] private static partial Regex TablesBlock();
}
