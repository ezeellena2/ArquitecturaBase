using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// AddWhatsAppAssistantAgent: the agent, the conversations that need a person, the why of the replies and the other actions of a reply.
// Going back refuses while any of them exists, before any DDL, and never deletes them.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantAgentMigrationTests(ApiFactory factory)
{
    private const string Migration = "20261112021526_AddWhatsAppAssistantAgent";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_agent_and_a_handoff_refuse_the_downgrade_and_without_them_it_goes_back_and_up_again()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("assistant_agent_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var migrations = db.Database.GetMigrations().ToList();
            var previous = migrations[migrations.IndexOf(Migration) - 1];
            var agent = WhatsAppAssistantAgent.Create("es");
            var contact = WhatsAppContact.Create(MetaWebhook.UniqueWaId(), MetaWebhook.UniqueBsuid(), "Persona ficticia", now);
            contact.HandOff("case", "Pide hablar con una persona.", now);
            db.AddRange(agent, contact);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            var withAgent = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct));
            Assert.Equal("P0001", withAgent.SqlState);
            db.Set<WhatsAppAssistantAgent>().Remove(await db.Set<WhatsAppAssistantAgent>().SingleAsync(Ct));
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            var withHandoff = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct));
            Assert.Equal("P0001", withHandoff.SqlState);
            Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync(Ct));
            var kept = await db.Set<WhatsAppContact>().AsNoTracking().SingleAsync(item => item.Id == contact.Id, Ct);
            Assert.Equal(("case", "Pide hablar con una persona."), (kept.HandoffReasonCode, kept.HandoffReason));

            var handedOff = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contact.Id, Ct);
            handedOff.ClearHandOff();
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            await db.GetService<IMigrator>().MigrateAsync(previous, Ct);
            Assert.DoesNotContain(Migration, await db.Database.GetAppliedMigrationsAsync(Ct));

            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var back = await db.Set<WhatsAppContact>().AsNoTracking().SingleAsync(item => item.Id == contact.Id, Ct);
            Assert.Equal(("Persona ficticia", false), (back.ProfileName, back.NeedsPerson));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }
}
