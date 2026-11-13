using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Task 9 of the documents: AddWhatsAppDocumentQuestions only adds the button of a list, the document question with more forms than three
// buttons hold. The previous schema cannot keep it, so its Down refuses while a list keeps one; without lists it goes back, and going up
// again keeps the other outgoing messages as they were.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppDocumentQuestionsMigrationTests(ApiFactory factory)
{
    private const string Migration = "20261113011825_AddWhatsAppDocumentQuestions";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_list_refuses_the_downgrade_and_without_lists_it_goes_back_and_up_again()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_document_questions_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var migrations = db.Database.GetMigrations().ToList();
            var previous = migrations[migrations.IndexOf(Migration) - 1];
            var waId = MetaWebhook.UniqueWaId();
            var contact = WhatsAppContact.Create(waId, MetaWebhook.UniqueBsuid(), "Persona ficticia", now);
            var list = WhatsAppOutgoingMessage.CreateAssistantList(Guid.NewGuid(), contact.Id, Guid.NewGuid(), "No reconocí este documento.",
                "Elegir formulario", [new("FORM_DOCUMENT:pagos", "Pagos", 0), new("FORM_DOCUMENT:other", "Otro", 0)], now, 1, waId, 1);
            var text = WhatsAppOutgoingMessage.CreateAssistantText(Guid.NewGuid(), contact.Id, Guid.NewGuid(), "Recibí tu documento.", now, 1, waId, 1);
            db.AddRange(contact, list, text);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            var withList = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(previous, Ct));
            Assert.Equal("P0001", withList.SqlState);
            Assert.Contains(Migration, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal("Elegir formulario", (await db.Set<WhatsAppOutgoingMessage>().AsNoTracking().SingleAsync(item => item.Id == list.Id, Ct)).ListButton);

            await db.Database.ExecuteSqlAsync($"""DELETE FROM "WhatsAppOutgoingMessages" WHERE "Id" = {list.Id}""", Ct);
            await db.GetService<IMigrator>().MigrateAsync(previous, Ct);
            Assert.DoesNotContain(Migration, await db.Database.GetAppliedMigrationsAsync(Ct));

            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var back = await db.Set<WhatsAppOutgoingMessage>().AsNoTracking().SingleAsync(item => item.Id == text.Id, Ct);
            Assert.Equal((WhatsAppOutgoingMessageKind.Text, "Recibí tu documento.", (string?)null), (back.Kind, back.Body, back.ListButton));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }
}
