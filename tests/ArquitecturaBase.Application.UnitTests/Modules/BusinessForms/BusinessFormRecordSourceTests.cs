using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormRecordSourceTests
{
    [Fact]
    public async Task A_revoked_reader_gets_no_receipts_or_values()
    {
        var reader = new FakeReader();
        var permissions = new MutablePermissionService { Allowed = false };
        var source = new BusinessFormRecordSource(reader, permissions);

        var result = await source.ReadAsync(Guid.NewGuid(), "es", [Guid.NewGuid()], CancellationToken.None);

        Assert.False(result.CanReadRecords);
        Assert.Empty(result.Replies);
        Assert.Equal(0, reader.ReadCount);
    }

    [Fact]
    public async Task A_permission_revoked_while_receipts_are_read_discards_the_projection()
    {
        var permissions = new MutablePermissionService { Allowed = true };
        var receiptId = Guid.NewGuid();
        var reader = new FakeReader
        {
            Rows = [MinimalRow(receiptId)],
            BeforeReturn = () => permissions.Allowed = false,
        };
        var source = new BusinessFormRecordSource(reader, permissions);

        var result = await source.ReadAsync(Guid.NewGuid(), "es", [receiptId], CancellationToken.None);

        Assert.False(result.CanReadRecords);
        Assert.Empty(result.Replies);
    }

    [Fact]
    public async Task An_authorized_projection_preserves_false_zero_absence_historical_labels_and_number()
    {
        var boolId = Guid.NewGuid();
        var decimalId = Guid.NewGuid();
        var absentId = Guid.NewGuid();
        var choiceId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var schema = new FormVersionSchema(1,
        [
            Field(boolId, "boolean", "boolean"),
            Field(decimalId, "decimal", "decimal"),
            Field(absentId, "optional", "shortText", required: false),
            Field(choiceId, "choice", "choice", options: [new CanonicalFormChoice(optionId, Text("Opción histórica", "Historical choice"))]),
        ]);
        var values = JsonSerializer.Serialize(new object[]
        {
            new { fieldId = boolId, value = false },
            new { fieldId = decimalId, value = 0m },
            new { fieldId = choiceId, value = optionId },
        });
        var receiptId = Guid.NewGuid();
        var reader = new FakeReader
        {
            Rows = [new(receiptId, Guid.NewGuid(), 24, Guid.NewGuid(), "VIS", Guid.NewGuid(), 3,
                "{\"es\":\"Visita\",\"en\":\"Visit\"}", JsonSerializer.Serialize(schema), values)],
        };
        var source = new BusinessFormRecordSource(reader, new MutablePermissionService { Allowed = true });

        var result = await source.ReadAsync(Guid.NewGuid(), "en", [receiptId], CancellationToken.None);

        Assert.True(result.CanReadRecords);
        var reply = Assert.Single(result.Replies).Value;
        Assert.Equal("VIS-000024", reply.Number);
        Assert.Equal("Visit", reply.Name);
        Assert.False(Assert.IsType<bool>(reply.Values[0].Value));
        Assert.Equal("0", reply.Values[1].Value);
        Assert.Null(reply.Values[2].Value);
        var choice = Assert.IsType<StructuredRecordChoiceValue>(reply.Values[3].Value);
        Assert.Equal(optionId.ToString("D"), choice.ChoiceId);
        Assert.Equal("Historical choice", choice.Label);
    }

    private static CanonicalFormField Field(Guid id, string key, string type, bool required = true,
        IReadOnlyList<CanonicalFormChoice>? options = null) =>
        new(id, key, type, Text(key, key), null, required, 100, options);

    [Fact]
    public async Task A_catalog_choice_uses_the_saved_offer_text_even_in_another_ui_language()
    {
        var fieldId = Guid.NewGuid(); var itemId = Guid.NewGuid(); var catalogId = Guid.NewGuid();
        var recordId = Guid.NewGuid(); var receiptId = Guid.NewGuid();
        var field = Field(fieldId, "person", "choice") with { Source = new(catalogId) };
        var schema = JsonSerializer.Serialize(new FormVersionSchema(1, [field]));
        var reader = new FakeReader { Rows = [new(receiptId, recordId, 1, Guid.NewGuid(), "FORM", Guid.NewGuid(), 1,
            "{\"es\":\"Formulario\",\"en\":\"Form\"}", schema,
            JsonSerializer.Serialize(new[] { new { fieldId, value = itemId } }),
            [new(recordId, fieldId, catalogId, 4, itemId, "Texto recibido", "Detalle recibido", "Personas")])] };
        var source = new BusinessFormRecordSource(reader, new MutablePermissionService { Allowed = true });
        var result = await source.ReadAsync(Guid.NewGuid(), "en", [receiptId], CancellationToken.None);
        var value = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(Assert.Single(result.Replies).Value.Values).Value);
        Assert.Equal("Texto recibido", value.Label);
        Assert.Equal("Detalle recibido", value.Detail);
        Assert.Equal(catalogId, value.CatalogId);
        Assert.Equal("Personas", value.CatalogName);
    }

    [Fact]
    public async Task File_metadata_is_read_from_the_committed_response_and_contains_no_storage_key()
    {
        var receipt = Guid.CreateVersion7(); var record = Guid.CreateVersion7(); var field = Guid.CreateVersion7(); var file = Guid.CreateVersion7();
        var question = Field(field, "photo", "photo") with { Files = new(1, 3, "camera") };
        var row = new FormRecordStoredRow(receipt, record, 1, Guid.CreateVersion7(), "PHOTO", Guid.CreateVersion7(), 1,
            "{\"es\":\"Fotos\",\"en\":\"Photos\"}", JsonSerializer.Serialize(new FormVersionSchema(1, [question])), "[]",
            Files: [new(record, field, file, 0, "original.png", "image/png", 123, true)]);
        var source = new BusinessFormRecordSource(new FakeReader { Rows = [row] }, new MutablePermissionService { Allowed = true });
        var response = await source.ReadAsync(Guid.CreateVersion7(), "es", [receipt], CancellationToken.None);
        var files = Assert.IsAssignableFrom<IReadOnlyList<StructuredRecordFileValue>>(Assert.Single(response.Replies[receipt].Values).Value);
        Assert.Equal("original.png", Assert.Single(files).Name);
        Assert.Equal(file, files[0].FileId);
        Assert.DoesNotContain("original.png", files[0].ToString());
    }

    private static Dictionary<string, string> Text(string es, string en) => new()
        { ["es"] = es, ["en"] = en };

    private static FormRecordStoredRow MinimalRow(Guid receiptId)
    {
        var schema = new FormVersionSchema(1, [Field(Guid.NewGuid(), "answer", "shortText")]);
        return new(receiptId, Guid.NewGuid(), 1, Guid.NewGuid(), "FORM", Guid.NewGuid(), 1,
            "{\"es\":\"Formulario\"}", JsonSerializer.Serialize(schema), "[]");
    }

    private sealed class FakeReader : IFormRecordReader
    {
        public IReadOnlyList<FormRecordStoredRow> Rows { get; init; } = [];
        public Action? BeforeReturn { get; init; }
        public int ReadCount { get; private set; }
        public Task<IReadOnlyList<FormRecordStoredRow>> ListAsync(IReadOnlyCollection<Guid> sourceReceiptIds, IReadOnlyDictionary<Guid, FormResponseVersionRow>? versions,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            BeforeReturn?.Invoke();
            return Task.FromResult(Rows);
        }
    }

    private sealed class MutablePermissionService : IPermissionService
    {
        public bool Allowed { get; set; }
        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<string>>(Allowed ? [BusinessFormsPermissions.RecordsRead] : []);
        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(Allowed && permission == BusinessFormsPermissions.RecordsRead);
        public Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
