using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiBusinessInformationMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Adding_structured_information_preserves_legacy_facts_revision_audit_and_receipts()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("business_guided_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var id = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261003134826_AddAiBusinessInformation", Ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AiBusinessInformation" ("Id","Scope","Name","Description","Hours","Location","Services","Policies","Faq","Revision","CreatedAtUtc","CreatedBy")
                VALUES ({id},1,'Shop','Repairs','Old hours','Old location','Services','Policies','Old FAQ',7,{now},{operatorId});
                INSERT INTO "AiBusinessInformationChanges" ("Id","RequestId","OperatorId","Fingerprint","KeyId","Revision","CreatedAtUtc")
                VALUES ({receiptId},{requestId},{operatorId},'old-fingerprint','old-key',7,{now});
                """, Ct);
            await db.Database.MigrateAsync(Ct);
            var row = await db.Set<AiBusinessInformation>().SingleAsync(Ct);
            Assert.Equal(id, row.Id);
            Assert.Equal("Old hours", row.Hours);
            Assert.Equal("Old location", row.Location);
            Assert.Equal("Old FAQ", row.Faq);
            Assert.Equal(7, row.Revision);
            Assert.Equal(operatorId, row.CreatedBy);
            Assert.Equal(now, row.CreatedAtUtc);
            Assert.Null(row.Details);
            var receipt = await db.Set<AiBusinessInformationChange>().SingleAsync(Ct);
            Assert.Equal(requestId, receipt.RequestId);
            Assert.Equal("old-fingerprint", receipt.Fingerprint);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }
}
