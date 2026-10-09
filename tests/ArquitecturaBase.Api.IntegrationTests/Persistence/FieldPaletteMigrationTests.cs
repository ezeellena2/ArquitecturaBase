using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

[Collection(ApiTestGroup.Name)]
public sealed class FieldPaletteMigrationTests(ApiFactory factory)
{
    private const string Before = "20261002155525_WhatsAppOperatorInbox";
    private static readonly DateTime CreatedAt = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_default_palette_migrates_both_ways_without_overwriting_a_custom_color(bool custom)
    {
        await using var database = factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("fieldpalette")));
        await using var scope = database.Services.CreateAsyncScope();
        await using var dbContext = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = dbContext.GetService<IMigrator>();
        var primary = custom ? "#335F85" : "#176b4c";
        try
        {
            await migrator.MigrateAsync(Before, Ct);
            await dbContext.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "SystemSettings"
                    ("Id", "RegistrationMode", "DefaultCulture", "DefaultTimeZoneId", "DefaultPageSize", "Revision",
                     "PrimaryColor", "MenuColor", "HeaderColor", "AlternateRowColor", "CreatedAtUtc")
                VALUES ({SystemSettings.SingletonId}, 'Open', 'en', 'UTC', 50, 7,
                        {primary}, '#194F3A', '#D0E5D5', '#F2F7F3', {CreatedAt})
                """, Ct);

            await migrator.MigrateAsync(cancellationToken: Ct);
            var migrated = await dbContext.SystemSettings.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(custom ? primary : "#285A3D", migrated.PrimaryColor);
            Assert.Equal(custom ? "#194F3A" : "#24352B", migrated.MenuColor);
            Assert.Equal(custom ? "#D0E5D5" : "#DFE8D9", migrated.HeaderColor);
            Assert.Equal(custom ? "#F2F7F3" : "#F4F6F0", migrated.AlternateRowColor);
            Assert.Equal(custom ? 7 : 8, migrated.Revision);
            Assert.Equal("en", migrated.DefaultCulture);
            Assert.Equal("UTC", migrated.DefaultTimeZoneId);
            Assert.Equal(50, migrated.DefaultPageSize);
            Assert.Equal(RegistrationMode.Open, migrated.RegistrationMode);
            Assert.Equal(CreatedAt, migrated.CreatedAtUtc);
            Assert.Equal(!custom, migrated.ModifiedAtUtc.HasValue);

            await migrator.MigrateAsync(Before, Ct);
            var restored = await dbContext.SystemSettings.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(custom ? primary : "#176B4C", restored.PrimaryColor);
            Assert.Equal("#194F3A", restored.MenuColor);
            Assert.Equal("#D0E5D5", restored.HeaderColor);
            Assert.Equal("#F2F7F3", restored.AlternateRowColor);
            Assert.Equal(custom ? 7 : 9, restored.Revision);
            Assert.Equal(50, restored.DefaultPageSize);
        }
        finally { await dbContext.Database.EnsureDeletedAsync(Ct); }
    }
}
