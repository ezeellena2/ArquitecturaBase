using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// DOC1, «Cómo viene leyendo» in PostgreSQL: only the responses of the form whose capture origin carries the document they came from,
// inside the window, count; per question, what the reading prefilled (proposed and included) against what was changed when confirming, and the latest
// corrections with the value read and the one confirmed. The capture of a document (task 9) writes that origin; here it is seeded.
[Collection(ApiTestGroup.Name)]
public sealed class FormDocumentReadingQueryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_numbers_and_corrections_come_from_the_documents_of_the_form_inside_the_window()
    {
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var (account, amount, date, bank) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var (definitionId, versionId) = await InsertFormAsync();
        var galicia = Guid.CreateVersion7();
        var corrected = await InsertRecordAsync(definitionId, versionId, 1, now.AddDays(-1), document: true,
            proposed: [(account, "2850590940090418135209"), (amount, "100"), (date, "2026-11-01"), (bank, galicia.ToString("D"))], changed: [account, bank],
            confirmed: [(account, "2850590940090418135201"), (amount, 100m), (date, "2026-11-01"), (bank, Guid.CreateVersion7().ToString("D"))],
            proposedReferences: [(bank, galicia, "Banco de Galicia")]);
        // The date was read but not prefilled (the person left it out), so it was neither read into the form nor kept.
        await InsertRecordAsync(definitionId, versionId, 2, now.AddDays(-2), document: true,
            proposed: [(account, "0170099220000067797370"), (amount, "200"), (date, "2026-11-03")], changed: [],
            confirmed: [(account, "0170099220000067797370"), (amount, 200m), (date, "2026-11-05")], included: [account, amount]);
        // Typed in the app, and read more than 30 days ago: neither counts.
        await InsertRecordAsync(definitionId, versionId, 3, now.AddDays(-1), document: false,
            proposed: [(account, "1")], changed: [account], confirmed: [(account, "2")]);
        await InsertRecordAsync(definitionId, versionId, 4, now.AddDays(-31), document: true,
            proposed: [(amount, "300")], changed: [amount], confirmed: [(amount, 301m)]);

        var (stats, corrections) = await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IFormDocumentReadingReader>();
            Assert.StartsWith("Q", (await reader.FindDefinitionAsync(definitionId, Ct))!.Prefix, StringComparison.Ordinal);
            Assert.Null(await reader.FindDefinitionAsync(Guid.CreateVersion7(), Ct));
            Assert.Single(await reader.ListVersionSchemasAsync([versionId, Guid.CreateVersion7()], Ct));
            return (await reader.FindStatsAsync(definitionId, now.AddDays(-30), Ct), await reader.ListCorrectionsAsync(definitionId, now.AddDays(-30), 10, Ct));
        });

        Assert.Equal(2, stats.Documents);
        Assert.Equal(new[] { (account, 2, 1), (amount, 2, 2), (date, 1, 1), (bank, 1, 0) }.OrderBy(row => row.Item1),
            stats.Questions.OrderBy(question => question.QuestionId).Select(question => (question.QuestionId, question.Read, question.Unchanged)));
        // The two corrections of the same response, by question; the option proposed shows the text its capture kept.
        Assert.Equal(new[] { (account, (string?)"2850590940090418135209", (string?)null), (bank, galicia.ToString("D"), "Banco de Galicia") }.OrderBy(row => row.Item1),
            corrections.OrderBy(correction => correction.FieldId).Select(correction => (correction.FieldId, correction.Read, correction.ReadText)));
        Assert.All(corrections, correction => Assert.Equal((corrected, 1L, versionId), (correction.RecordId, correction.Number, correction.VersionId)));
        Assert.Equal("2850590940090418135201", corrections.Single(correction => correction.FieldId == account).Confirmed);
        Assert.Equal(now.AddDays(-1), corrections[0].SubmittedAtUtc, TimeSpan.FromMilliseconds(1));
    }

    private async Task<(Guid DefinitionId, Guid VersionId)> InsertFormAsync()
    {
        var definitionId = Guid.CreateVersion7(); var versionId = Guid.CreateVersion7();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var prefix = "Q" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        const string names = """{"es":"Comprobantes"}""";
        const string submit = """{"es":"Enviar"}""";
        const string draft = """{"format":1,"fields":[]}""";
        const string schema = """{"format":1,"questions":[]}""";
        var fingerprint = new string('f', 64);
        await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormDefinitions" ("Id", "Name", "NameJson", "Prefix", "Culture", "SubmitLabelJson", "SchemaJson",
                "Revision", "QuestionCount", "PrefixLocked", "LastRecordNumber", "CreatedAtUtc", "IsDeleted", "AppOnly")
            VALUES ({definitionId}, 'Comprobantes', CAST({names} AS jsonb), {prefix}, 'es', CAST({submit} AS jsonb),
                CAST({draft} AS jsonb), 1, 0, TRUE, 4, {now}, FALSE, TRUE);
            INSERT INTO "BusinessFormVersions" ("Id", "DefinitionId", "Number", "ContentHash", "SchemaFormatVersion", "SchemaJson",
                "NameJson", "SubmitLabelJson", "Culture", "QuestionCount", "CreatedAtUtc", "PublishedCultures", "AppOnly")
            VALUES ({versionId}, {definitionId}, 1, {fingerprint}, 1, CAST({schema} AS jsonb), CAST({names} AS jsonb),
                CAST({submit} AS jsonb), 'es', 0, {now}, ARRAY['es'], TRUE);
            """, Ct));
        return (definitionId, versionId);
    }

    // A response with the origin a capture keeps: the values proposed, the ones prefilled (all, unless the test says), the questions
    // changed and, when it came from one, its document.
    private async Task<Guid> InsertRecordAsync(Guid definitionId, Guid versionId, long number, DateTime submittedAtUtc, bool document,
        (Guid FieldId, string Value)[] proposed, Guid[] changed, (Guid FieldId, object Value)[] confirmed, (Guid FieldId, Guid ItemId, string Text)[]? proposedReferences = null,
        Guid[]? included = null)
    {
        var recordId = Guid.CreateVersion7();
        var origin = JsonSerializer.Serialize(new
        {
            origin = new
            {
                senderKind = "Assistant", operatorUserId = (Guid?)null, reason = (string?)null, proposal = (object?)null,
                proposedValues = proposed.Select(value => new { fieldId = value.FieldId, type = "shortText", value = value.Value }).ToArray(),
                includedFieldIds = included ?? proposed.Select(value => value.FieldId).ToArray(),
                document = document ? (object)new { documentId = Guid.CreateVersion7() } : null,
            },
            changedFieldIds = changed,
            proposedReferences = (proposedReferences ?? []).Select(reference => new { fieldId = reference.FieldId, itemId = reference.ItemId, text = reference.Text }).ToArray(),
        }, JsonSerializerOptions.Web);
        var values = JsonSerializer.Serialize(confirmed.Select(value => new { fieldId = value.FieldId, value = value.Value }).ToArray(), JsonSerializerOptions.Web);
        await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlAsync($"""
            INSERT INTO "BusinessFormRecords" ("Id", "DefinitionId", "VersionId", "Number", "ActorUserId", "SourceReceiptId",
                "SubmittedAtUtc", "ValuesJson", "CreatedAtUtc", "State", "EnteredInApp", "CaptureOriginJson")
            VALUES ({recordId}, {definitionId}, {versionId}, {number}, {Guid.CreateVersion7()}, {Guid.CreateVersion7()}, {submittedAtUtc},
                CAST({values} AS jsonb), {submittedAtUtc}, 'Received', FALSE, CAST({origin} AS jsonb));
            """, Ct));
        return recordId;
    }
}
