using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
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

public sealed class FormCatalogFieldExportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("text", "=SUM(A1)", "text")]
    [InlineData("integer", "123456789012345678", "integer")]
    [InlineData("decimal", "0.1234567890123456789012345678", "decimal")]
    [InlineData("date", "2026-10-23", "date")]
    [InlineData("boolean", "false", "boolean")]
    [InlineData("catalog", "source-choice", "text")]
    public void A_single_extra_field_keeps_its_excel_type(string type, string canonical, string kind)
    {
        var field = Guid.NewGuid();
        var choice = new StructuredRecordChoiceValue("choice", "Supplier", Fields: [new(field, "Extra", type, "data", canonical, type == "catalog" ? "Saved source text" : null)]);
        var cell = new FormResponseCell(Guid.NewGuid(), "choice", "Answered", choice);
        var exported = FormExportPolicy.CatalogField(cell, field);
        Assert.Equal(kind, exported.Kind);
        switch (type)
        {
            case "integer": Assert.Equal(123456789012345678L, Assert.IsType<long>(exported.Value)); break;
            case "decimal": Assert.Equal(0.1234567890123456789012345678m, Assert.IsType<decimal>(exported.Value)); break;
            case "date": Assert.Equal(new DateTime(2026, 10, 23), Assert.IsType<DateTime>(exported.Value)); break;
            case "boolean": Assert.False(Assert.IsType<bool>(exported.Value)); break;
            case "catalog": Assert.Equal("Saved source text", exported.Value); break;
            default: Assert.Equal(canonical, exported.Value); break;
        }
    }

    [Fact]
    public void A_missing_extra_field_is_distinct_from_an_empty_question_and_keeps_multi_choice_order()
    {
        var field = Guid.NewGuid(); var question = Guid.NewGuid();
        var first = new StructuredRecordChoiceValue("one", "First", Fields: [new(field, "Days", "integer", "data", "123456789012345678")]);
        var second = new StructuredRecordChoiceValue("two", "Second", Fields: [new(field, "Days", "integer", "data", "0")]);
        Assert.Equal("—", FormExportPolicy.CatalogField(new(question, "choice", "Answered", new StructuredRecordChoiceValue("old", "Legacy")), field).Value);
        Assert.Null(FormExportPolicy.CatalogField(new(question, "choice", "Unanswered", null), field).Value);
        Assert.Equal("—", FormExportPolicy.CatalogField(new(question, "choice", "NotAsked", null), field).Value);
        var multiple = FormExportPolicy.CatalogField(new(question, "multiChoice", "Answered", new[] { second, first }), field);
        Assert.Equal("text", multiple.Kind);
        Assert.Equal("0; 123456789012345678", multiple.Value);
    }

    [Theory]
    [InlineData("es", 0)]
    [InlineData("es", 1)]
    [InlineData("en", 0)]
    [InlineData("en", 1)]
    public async Task All_extra_fields_have_headers_and_scalar_types_even_before_responses_exist(string culture, int count)
    {
        var previous = CultureInfo.CurrentUICulture; CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var ports = Ports(count);
            var result = await ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), Ct);
            Assert.True(result.IsSuccess);
            Assert.Equal(culture == "es" ? new[] { "Proveedor", "Proveedor · Días", "Proveedor · Zona" }
                : ["Supplier", "Supplier · Days", "Supplier · Zona"], ports.Writer.Request!.Headers.Skip(4));
            Assert.Equal(count, ports.Writer.Request.Rows.Count);
            if (count == 1)
            {
                var row = Assert.Single(ports.Writer.Request.Rows);
                Assert.Equal("integer", row[5].Kind); Assert.Equal(30L, row[5].Value);
                Assert.Equal("Center", row[6].Value);
            }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task Visible_extra_columns_are_selected_per_question_independently_of_the_base_column()
    {
        var ports = Ports(1); var selected = new[] { new FormExportCatalogField(ports.Question, ports.Days) };
        var result = await ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), [], selected), Ct);
        Assert.True(result.IsSuccess);
        Assert.Single(ports.Writer.Request!.Headers.Skip(4));
        var row = Assert.Single(ports.Writer.Request.Rows); Assert.Equal(5, row.Count); Assert.Equal(30L, row[4].Value);
        Assert.DoesNotContain(ports.Writer.Request.Headers, header => header.EndsWith(" · Zona", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_explicit_empty_extra_selection_exports_only_selected_questions()
    {
        var ports = Ports(1);
        var result = await ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), [ports.Question], []), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(5, ports.Writer.Request!.Headers.Count);
        Assert.Equal("Supplier", Assert.Single(ports.Writer.Request.Rows)[4].Value);
    }

    [Fact]
    public async Task An_unknown_extra_selection_is_rejected_before_reading_records_or_writing_bytes()
    {
        var ports = Ports(1);
        var result = await ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null, [new(ports.Question, Guid.NewGuid())]), Ct);
        Assert.True(result.IsFailure); Assert.Equal(FormRecordErrors.FilterInvalid, result.Error);
        Assert.Empty(ports.Reader.ExportBatchSizes); Assert.Null(ports.Writer.Request);
    }

    [Fact]
    public void Duplicate_or_empty_extra_selections_fail_validation()
    {
        var validator = new FormExportRequestValidator(); var question = Guid.NewGuid(); var field = Guid.NewGuid();
        Assert.False(validator.Validate(new FormExportRequest(new(), null, [new(question, field), new(question, field)])).IsValid);
        Assert.False(validator.Validate(new FormExportRequest(new(), null, [new(Guid.Empty, field)])).IsValid);
        Assert.False(validator.Validate(new FormExportRequest(new(), null, [null])).IsValid);
        Assert.False(validator.Validate(new FormExportRequest(new(), null, [new(question, Guid.Empty)])).IsValid);
        Assert.True(validator.Validate(new FormExportRequest(new(), null, [new(question, field), new(Guid.NewGuid(), field)])).IsValid);
    }

    private static (FormExportService Service, FakeFormWorkbookWriter Writer, FakeFormResponseReader Reader, Guid Question, Guid Days) Ports(int count)
    {
        var actor = new FakeCurrentUser { UserId = Guid.NewGuid() }; var permissions = new FakePermissionService();
        permissions.Permissions[actor.UserId.Value] = [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport];
        var reader = new FakeFormResponseReader(); reader.ArrangeExport(count);
        var questionId = Guid.NewGuid(); var catalog = Guid.NewGuid(); var item = Guid.NewGuid(); var days = Guid.NewGuid(); var zone = Guid.NewGuid();
        var field = new CanonicalFormField(questionId, "supplier", "choice", Text("Proveedor", "Supplier"), null, false, null, null, Source: new(catalog));
        var initial = reader.Definition!; var version = initial.Versions[0] with { SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [field])) };
        var extra = new[] { new FormResponseCatalogFieldRow(days, "days", Text("Días", "Days"), "integer", "data"),
            new FormResponseCatalogFieldRow(zone, "zone", new Dictionary<string, string> { ["es"] = "Zona" }, "catalog", "filter") };
        reader.Definition = new(initial.Id, initial.Prefix, version.Id, [version], CatalogFields: new Dictionary<Guid, IReadOnlyList<FormResponseCatalogFieldRow>> { [catalog] = extra });
        var fields = new[] { new FormCatalogCapturedField(days, "days", Text("Días", "Days"), "integer", "data", JsonSerializer.SerializeToElement(30)),
            new FormCatalogCapturedField(zone, "zone", Text("Zona", "Zone"), "catalog", "filter", JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D")), Text("Center", "Center")) };
        reader.ExportRows = reader.ExportRows.Select(row => row with { Version = version, Record = row.Record with
        {
            SchemaJson = version.SchemaJson,
            ValuesJson = JsonSerializer.Serialize(new[] { new { fieldId = questionId, value = item } }),
            References = [new(row.Record.RecordId, questionId, catalog, 1, item, "Supplier", null, "Suppliers", FieldValuesJson: FormDraftPolicy.Serialize(fields))],
        } }).ToArray();
        var writer = new FakeFormWorkbookWriter();
        var service = new FormExportService(reader, writer, new(actor, permissions),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()), new FakeTimeProvider(), NullLogger<FormExportService>.Instance, new([]));
        return (service, writer, reader, questionId, days);
    }
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
}
