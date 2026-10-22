using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMultipleChoiceTypedExportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Integer", "123456789012345678", "0")]
    [InlineData("Decimal", "0.1234567890123456789012345678", "-1500.5")]
    [InlineData("Date", "2026-10-22", "2026-10-23")]
    public async Task Multiple_typed_values_use_exact_canonical_text_for_empty_single_and_many_selections(string type, string firstValue, string secondValue)
    {
        var ports = Ports(type, 3);
        var field = ports.Field; var first = Guid.NewGuid(); var second = Guid.NewGuid();
        ports.Reader.ExportRows = ports.Reader.ExportRows.Select((row, index) =>
        {
            var selected = index switch { 0 => Array.Empty<Guid>(), 1 => [first], _ => new[] { second, first } };
            var references = selected.Select(id => new FormRecordReferenceRow(row.Record.RecordId, field.FieldId, field.Source!.Catalog!.Value, 3,
                id, id == first ? "Primera" : "Segunda", null, "Tipos", Value: id == first ? firstValue : secondValue, ValueType: type)).ToArray();
            return row with { Record = row.Record with { ValuesJson = JsonSerializer.Serialize(new[] { new { fieldId = field.FieldId, value = selected } }), References = references } };
        }).ToArray();
        var result = await ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), Ct);
        Assert.True(result.IsSuccess);
        var workbook = ports.Writer.Request!;
        Assert.Equal(6, workbook.Headers.Count); Assert.Equal(3, workbook.Rows.Count);
        Assert.All(workbook.Rows, row => Assert.Equal("text", row[5].Kind));
        Assert.Null(workbook.Rows[0][5].Value);
        Assert.Equal(firstValue, workbook.Rows[1][5].Value);
        Assert.Equal(secondValue + "; " + firstValue, workbook.Rows[2][5].Value);
        Assert.Equal("Segunda; Primera", workbook.Rows[2][4].Value);
        Assert.Equal(1, ports.Reader.DefinitionReads); Assert.Single(ports.Reader.ExportBatchSizes);
    }

    [Theory]
    [InlineData("Integer")]
    [InlineData("Decimal")]
    [InlineData("Date")]
    public async Task A_multiple_typed_catalog_keeps_both_headers_before_any_response_exists(string type)
    {
        var ports = Ports(type, 0);
        var result = await ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(6, ports.Writer.Request!.Headers.Count);
        Assert.Contains(ports.Writer.Request.Headers, header => header.StartsWith("Riesgos · ", StringComparison.Ordinal));
        Assert.Empty(ports.Writer.Request.Rows); Assert.Empty(ports.Reader.ExportBatchSizes);
        Assert.Equal(1, ports.Reader.DefinitionReads);
    }

    private static (FormExportService Service, FakeFormWorkbookWriter Writer, FakeFormResponseReader Reader, CanonicalFormField Field) Ports(string type, int count)
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() }; var permissions = new FakePermissionService();
        permissions.Permissions[user.UserId.Value] = [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport];
        var reader = new FakeFormResponseReader(); reader.ArrangeExport(count);
        var catalog = Guid.NewGuid(); var field = new CanonicalFormField(Guid.NewGuid(), "risks", "multiChoice",
            new Dictionary<string, string> { ["es"] = "Riesgos", ["en"] = "Riesgos" }, null, false, null, null,
            Source: new(catalog), Selection: new(0, 3));
        var original = reader.Definition!; var version = original.Versions[0] with { SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [field])) };
        reader.Definition = new(original.Id, original.Prefix, version.Id, [version], CatalogValueTypes: new Dictionary<Guid, string> { [catalog] = type });
        reader.ExportRows = reader.ExportRows.Select(row => row with { Version = version, Record = row.Record with { SchemaJson = version.SchemaJson } }).ToArray();
        var writer = new FakeFormWorkbookWriter();
        var service = new FormExportService(reader, writer, new(user, permissions),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()), new FakeTimeProvider(), NullLogger<FormExportService>.Instance, new([]));
        return (service, writer, reader, field);
    }
}
