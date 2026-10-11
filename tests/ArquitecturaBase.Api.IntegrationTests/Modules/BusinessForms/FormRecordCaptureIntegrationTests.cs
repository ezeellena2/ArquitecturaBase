using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Real_capture_is_typed_idempotent_and_readable_from_postgres()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        var actorId = Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var sourceReceiptId = Guid.NewGuid();
        var prefix = "R" + Guid.NewGuid().ToString("N")[..8];
        var choiceId = Guid.NewGuid();
        var fields = new[]
        {
            Field("shortText", required: true), Field("decimal", required: true), Field("date", required: true),
            Field("boolean", required: true), Field("choice", required: true, [new(choiceId, new Dictionary<string, string> { ["es"] = "Rojo", ["en"] = "Red" })]),
            Field("longText", required: false),
        };
        var draftSchema = JsonSerializer.Serialize(new FormQuestionSchema(1, fields), SerializerOptions);
        var publishedSchema = JsonSerializer.Serialize(new FormVersionSchema(1, fields), SerializerOptions);
        var definition = BusinessFormDefinition.Create();
        var definitionId = definition.Id;
        var version = BusinessFormVersion.Create(definitionId, 1, "test-hash");
        var versionId = version.Id;
        Assert.True(definition.SetDraft(1, "Registro", "{\"es\":\"Registro\",\"en\":\"Record\"}", prefix, "es",
            "{\"es\":\"Enviar\",\"en\":\"Submit\"}", draftSchema, fields.Length, creating: true).IsSuccess);
        version.SealSchema(publishedSchema);
        version.SealPresentation("{\"es\":\"Registro\",\"en\":\"Record\"}", "{\"es\":\"Enviar\",\"en\":\"Submit\"}", "es", fields.Length);

        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            foreach (var field in fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definitionId, field.Key, field.Type));
            await db.SaveChangesAsync(Ct);
            db.Set<BusinessFormVersion>().Add(version);
            Assert.True(definition.ActivateVersion(null, versionId).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });

        var answers = new Dictionary<string, string>
        {
            [fields[0].Key] = "Atención",
            [fields[1].Key] = "123.45",
            [fields[2].Key] = "2026-10-11",
            [fields[3].Key] = "false",
            [fields[4].Key] = choiceId.ToString("D"),
        };
        var capture = new StructuredRecordCaptureRequest(sourceReceiptId, definitionId, versionId, actorId,
            DateTime.SpecifyKind(new DateTime(2026, 10, 11, 12, 0, 0), DateTimeKind.Utc), answers);
        using var scope = factory.Services.CreateScope();
        var sink = scope.ServiceProvider.GetRequiredService<IStructuredRecordSink>();
        var invalidText = new StructuredRecordCaptureRequest(Guid.NewGuid(), definitionId, versionId, actorId,
            capture.SubmittedAtUtc, new Dictionary<string, string>(answers) { [fields[0].Key] = "antes\0después" });
        var rejected = await sink.CaptureAsync(invalidText, Ct);
        Assert.Equal("Rejected", rejected.State);
        Assert.Null(rejected.Number);

        var first = await sink.CaptureAsync(capture, Ct);
        var replay = await sink.CaptureAsync(capture, Ct);
        Assert.Equal("Completed", first.State);
        Assert.Equal(1, first.Number);
        Assert.Equal(first, replay);
        var priorReceipt = await sink.FindExistingReceiptAsync(capture, Ct);
        var changedReceipt = await sink.FindExistingReceiptAsync(capture with
        {
            Answers = new Dictionary<string, string>(answers) { [fields[0].Key] = "Alterada" },
        }, Ct);
        Assert.Equal(first, priorReceipt);
        Assert.Null(changedReceipt);
        Assert.Equal("Rejected", (await sink.CaptureAsync(capture with { Answers = new Dictionary<string, string>(answers) { [fields[0].Key] = "Alterada" } }, Ct)).State);

        var record = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().SingleAsync(row => row.Id == first.RecordId, Ct));
        Assert.Equal(1, record.Number);
        Assert.Equal(5, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordValue>().CountAsync(row => row.RecordId == record.Id, Ct)));
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCaptureReceipt>().CountAsync(row => row.SourceReceiptId == sourceReceiptId, Ct)));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecordValue>().Where(row => row.RecordId == record.Id && row.Type == "boolean")
            .Select(row => row.BooleanValue).SingleAsync(Ct)));

        var source = scope.ServiceProvider.GetRequiredService<IStructuredRecordSource>();
        var read = await source.ReadAsync(actorId, "es", [sourceReceiptId], Ct);
        Assert.True(read.CanReadRecords);
        var reply = Assert.Single(read.Replies.Values);
        Assert.Equal(prefix + "-000001", reply.Number);
        Assert.Equal("Rojo", Assert.IsType<StructuredRecordChoiceValue>(reply.Values.Single(value => value.Type == "choice").Value).Label);
        Assert.False(Assert.IsType<bool>(reply.Values.Single(value => value.Type == "boolean").Value));
        Assert.Null(reply.Values.Single(value => value.FieldId == fields[5].FieldId).Value);

        await Assert.ThrowsAsync<DbUpdateException>(() => factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id, definitionId, Guid.NewGuid(), "shortText", "x"));
            await db.SaveChangesAsync(Ct);
        }));
    }

    private static CanonicalFormField Field(string type, bool required, IReadOnlyList<CanonicalFormChoice>? options = null)
    {
        var id = Guid.NewGuid();
        return new(id, "field_" + id.ToString("N"), type,
            new Dictionary<string, string> { ["es"] = type, ["en"] = type }, null, required,
            type == "shortText" ? 80 : type == "longText" ? 600 : null, options);
    }
}
