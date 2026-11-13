using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// VC1-A, task 28: AddReceivedDocumentRows adds the sheet and the number of a row, the unique index with them and why a proposed one was not
// sent, filled in for the documents already kept for the team. Its Down refuses a row before any DDL instead of deleting it (the previous
// schema keeps one entry per channel source), and goes back once none is left, keeping each document as it was.
[Collection(ApiTestGroup.Name)]
public sealed class FormReceivedDocumentRowMigrationTests(ApiFactory factory)
{
    private const string Migration = "AddReceivedDocumentRows";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Down_refuses_a_row_goes_back_without_one_and_up_fills_in_why_a_document_was_not_sent()
    {
        var connection = factory.NewDatabaseConnectionString("form_received_document_rows");
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await migrator.MigrateAsync(This(db), Ct);
            var document = FormReceivedDocument.Receive(Guid.CreateVersion7(), "whatsapp", "wamid.document", Guid.NewGuid(), now, "forms/documents/a.pdf", "a.pdf",
                "application/pdf", 10, new string('a', 64), 1, null).Value;
            Assert.True(document.LeaveUnclassified("unknown", null, now).IsSuccess);
            var row = FormReceivedDocument.ReceiveRow(Guid.CreateVersion7(), "whatsapp", "wamid.rows", Guid.NewGuid(), now, "forms/documents/b.xlsx", "b.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 10, new string('b', 64), null, 2, 1, FormReceivedDocumentRowKind.Unmatched,
                null).Value;
            Assert.True(row.LeaveUnclassified("unknown", null, now).IsSuccess);
            db.AddRange(document, row);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            var applied = await db.Database.GetAppliedMigrationsAsync(Ct);

            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Before(db), Ct));

            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Equal(applied, await db.Database.GetAppliedMigrationsAsync(Ct));
            Assert.Equal(3, await ColumnsAsync(db));
            await db.Database.ExecuteSqlAsync($"""DELETE FROM "FormReceivedDocuments" WHERE "Id" = {row.Id}""", Ct);
            await migrator.MigrateAsync(Before(db), Ct);
            Assert.Equal(0, await ColumnsAsync(db));
            // A document the previous schema kept for the team, because its form could not be sent to its person.
            await db.Database.ExecuteSqlAsync($"""UPDATE "FormReceivedDocuments" SET "NotSendable" = TRUE WHERE "Id" = {document.Id}""", Ct);

            await migrator.MigrateAsync(cancellationToken: Ct);

            Assert.Equal(3, await ColumnsAsync(db));
            var kept = await db.Set<FormReceivedDocument>().AsNoTracking().SingleAsync(value => value.Id == document.Id, Ct);
            Assert.Equal((true, FormReceivedDocumentNotSentReason.NotSendable, (int?)null, "a.pdf"), (kept.NotSendable, kept.NotSentReason, kept.RowNumber, kept.FileName));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    // The migration right before this one, whichever it is.
    private static string Before(ApplicationDbContext db) =>
        db.Database.GetMigrations().TakeWhile(migration => !migration.EndsWith("_" + Migration, StringComparison.Ordinal)).Last();

    // This migration, with its timestamp.
    private static string This(ApplicationDbContext db) =>
        db.Database.GetMigrations().Single(migration => migration.EndsWith("_" + Migration, StringComparison.Ordinal));

    private static Task<int> ColumnsAsync(ApplicationDbContext db) => db.Database.SqlQueryRaw<int>("""
        SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
        WHERE table_name = 'FormReceivedDocuments' AND column_name IN ('SheetName', 'RowNumber', 'NotSentReason')
        """).SingleAsync(Ct);
}
