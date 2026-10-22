using System.Globalization;
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
public sealed class FormExportServiceTests
{
    [Theory]
    [InlineData(250)]
    [InlineData(10000)]
    public async Task Export_reads_one_ordered_snapshot_and_hydrates_batches_of_one_hundred(int count)
    {
        var ports = Create(true);
        ports.Reader.ArrangeExport(count);
        var result = await ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(count, ports.Writer.Request!.Rows.Count);
        Assert.Equal(1, ports.Reader.DefinitionReads);
        Assert.Equal(1, ports.Reader.ExportSnapshotReads);
        Assert.Equal(Enumerable.Range(0, (count + 99) / 100).Select(index => Math.Min(100, count - index * 100)), ports.Reader.ExportBatchSizes);
        Assert.Equal("T-000001", ports.Writer.Request.Rows[0][0].Value);
        Assert.Equal("T-" + count.ToString("D6", System.Globalization.CultureInfo.InvariantCulture), ports.Writer.Request.Rows[^1][0].Value);
    }
    [Fact]
    public async Task Export_projects_rows_using_the_definition_schema_for_their_version()
    {
        var ports = Create(true);
        ports.Reader.ArrangeExport(2);
        ports.Reader.ExportRows = ports.Reader.ExportRows.Select(row => row with { Record = row.Record with { SchemaJson = "not-read" } }).ToArray();
        var result = await ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, ports.Writer.Request!.Rows.Count);
    }
    [Fact]
    public async Task Export_excludes_a_new_version_captured_while_loading_the_definition()
    {
        var ports = Create(true);
        ports.Reader.ArrangeExport(1);
        ports.Reader.BeforeDefinitionReturn = () =>
        {
            var row = ports.Reader.ExportRows[0];
            ports.Reader.ExportRows = [row, row with { Record = row.Record with {
                RecordId = Guid.NewGuid(), SourceReceiptId = Guid.NewGuid(), Number = 2, VersionId = Guid.NewGuid(), VersionNumber = 2 } }];
        };
        var result = await ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Single(ports.Writer.Request!.Rows);
        Assert.Equal("T-000001", ports.Writer.Request.Rows[0][0].Value);
    }
    [Fact]
    public async Task Export_requires_its_own_permission_before_reading_responses()
    {
        var ports = Create(false);
        Assert.True((await ports.Service.ExportAsync(Guid.NewGuid(), new(new(), null), CancellationToken.None)).IsFailure);
        Assert.Equal(0, ports.Reader.Reads);
        Assert.Null(ports.Writer.Request);
    }
    [Fact]
    public async Task More_than_ten_thousand_responses_do_not_reach_the_workbook_writer()
    {
        var ports = Create(true);
        var result = await ports.Service.ExportAsync(Guid.NewGuid(), new(new(), null), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.Record.ExportTooLarge", result.Error.Code);
        Assert.Null(ports.Writer.Request);
    }
    [Fact]
    public async Task Revocation_before_returning_bytes_discards_the_export()
    {
        var ports = Create(true);
        ports.Reader.ArrangeExport(0);
        ports.Writer.BeforeReturn = () => ports.Permissions.Permissions.Clear();
        var result = await ports.Service.ExportAsync(Guid.NewGuid(), new(new(), []), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }
    [Theory]
    [InlineData("definition")]
    [InlineData("writer")]
    public async Task A_processing_deadline_discards_the_export_and_a_new_request_gets_a_fresh_budget(string phase)
    {
        var ports=Create(true);ports.Reader.ArrangeExport(1);
        if(phase=="definition")ports.Reader.BeforeDefinitionReturn=()=>ports.Clock.Advance(TimeSpan.FromSeconds(31));
        else ports.Writer.BeforeReturn=()=>ports.Clock.Advance(TimeSpan.FromSeconds(31));
        var expired=await ports.Service.ExportAsync(ports.Reader.Definition!.Id,new(new(),null),CancellationToken.None);
        Assert.True(expired.IsFailure);Assert.Equal(FormRecordErrors.ExportBudgetExceeded,expired.Error);
        if(phase=="definition")Assert.Null(ports.Writer.Request);
        ports.Reader.BeforeDefinitionReturn=null;ports.Writer.BeforeReturn=null;
        var fresh=await ports.Service.ExportAsync(ports.Reader.Definition.Id,new(new(),null),CancellationToken.None);
        Assert.True(fresh.IsSuccess);Assert.Single(ports.Writer.Request!.Rows);
    }
    [Fact]
    public async Task Caller_cancellation_is_not_misreported_as_an_export_processing_deadline()
    {
        var ports=Create(true);ports.Reader.ArrangeExport(1);using var caller=new CancellationTokenSource();
        ports.Writer.BeforeReturn=caller.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>ports.Service.ExportAsync(ports.Reader.Definition!.Id,new(new(),null),caller.Token));
    }
    [Fact]
    public async Task Caller_cancellation_keeps_its_meaning_when_the_processing_deadline_also_expires()
    {
        var ports = Create(true);
        ports.Reader.ArrangeExport(1);
        using var caller = new CancellationTokenSource();
        ports.Writer.BeforeReturn = () =>
        {
            caller.Cancel();
            ports.Clock.Advance(TimeSpan.FromSeconds(31));
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ports.Service.ExportAsync(ports.Reader.Definition!.Id, new(new(), null), caller.Token));
    }
    [Theory]
    [InlineData("Integer", "123456789012345678", "es", 0)]
    [InlineData("Integer", "123456789012345678", "es", 1)]
    [InlineData("Decimal", "1500.123456789012345678901234", "es", 0)]
    [InlineData("Decimal", "1500.123456789012345678901234", "es", 1)]
    [InlineData("Date", "2026-10-08", "es", 0)]
    [InlineData("Date", "2026-10-08", "es", 1)]
    [InlineData("Integer", "0", "en", 0)]
    [InlineData("Integer", "0", "en", 1)]
    [InlineData("Decimal", "0", "en", 0)]
    [InlineData("Decimal", "0", "en", 1)]
    [InlineData("Date", "2026-10-08", "en", 0)]
    [InlineData("Date", "2026-10-08", "en", 1)]
    public async Task Typed_catalog_exports_add_a_typed_value_column_even_without_rows(string valueType, string value, string culture, int count)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var ports = Create(true);
            ports.Reader.ArrangeExport(count);
            var catalogId = Guid.NewGuid();
            var fieldId = Guid.NewGuid();
            var itemId = Guid.NewGuid();
            var field = new CanonicalFormField(fieldId, "zone", "choice",
                new Dictionary<string, string> { ["es"] = "Zona", ["en"] = "Zone" }, null, false, null, null, new(catalogId));
            var initial = ports.Reader.Definition!;
            var version = initial.Versions.Single() with
            {
                SchemaJson = JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web)
            };
            ports.Reader.Definition = new(initial.Id, initial.Prefix, version.Id, [version],
                CatalogValueTypes: new Dictionary<Guid, string> { [catalogId] = valueType });
            ports.Reader.ExportRows = ports.Reader.ExportRows.Select(row => row with
            {
                Version = version,
                Record = row.Record with
                {
                    SchemaJson = version.SchemaJson,
                    ValuesJson = JsonSerializer.Serialize(new[] { new { fieldId, value = itemId } }),
                    References = [new(row.Record.RecordId, fieldId, catalogId, 1, itemId, "GBA", null, "Zonas",
                        Value: value, ValueType: valueType)]
                }
            }).ToArray();

            var result = await ports.Service.ExportAsync(initial.Id, new(new(), null), CancellationToken.None);

            Assert.True(result.IsSuccess);
            var request = ports.Writer.Request!;
            Assert.Equal(culture == "es" ? new[] { "Zona", "Zona · valor" } : ["Zone", "Zone · value"], request.Headers.Skip(4));
            Assert.Equal(count, request.Rows.Count);
            Assert.Equal(1, ports.Reader.DefinitionReads);
            if (count == 0) Assert.Empty(ports.Reader.ExportBatchSizes);
            else
            {
                var row = Assert.Single(request.Rows);
                Assert.Equal("GBA", row[4].Value);
                var typed = row[5];
                switch (valueType)
                {
                    case "Integer": Assert.Equal("integer", typed.Kind); Assert.Equal(long.Parse(value, CultureInfo.InvariantCulture), Assert.IsType<long>(typed.Value)); break;
                    case "Decimal": Assert.Equal("decimal", typed.Kind); Assert.Equal(decimal.Parse(value, CultureInfo.InvariantCulture), Assert.IsType<decimal>(typed.Value)); break;
                    case "Date": Assert.Equal("date", typed.Kind); Assert.Equal(new DateTime(2026, 10, 8), Assert.IsType<DateTime>(typed.Value)); break;
                }
            }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task Typed_catalog_value_columns_keep_missing_empty_and_zero_distinct()
    {
        var ports = Create(true);
        ports.Reader.ArrangeExport(3);
        var initial = ports.Reader.Definition!;
        var previous = initial.Versions.Single();
        var catalogId = Guid.NewGuid();
        var fieldId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var field = new CanonicalFormField(fieldId, "zone", "choice", new Dictionary<string, string> { ["es"] = "Zona", ["en"] = "Zone" },
            null, false, null, null, new(catalogId));
        var current = previous with { Id = Guid.NewGuid(), Number = 2,
            SchemaJson = JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web) };
        ports.Reader.Definition = new(initial.Id, initial.Prefix, current.Id, [previous, current],
            CatalogValueTypes: new Dictionary<Guid, string> { [catalogId] = "Decimal" });
        ports.Reader.ExportRows = ports.Reader.ExportRows.Select((row, index) => index == 0 ? row : row with
        {
            Version = current,
            Record = row.Record with
            {
                VersionId = current.Id, VersionNumber = current.Number, SchemaJson = current.SchemaJson,
                ValuesJson = index == 1 ? "[]" : JsonSerializer.Serialize(new[] { new { fieldId, value = itemId } }),
                References = index == 1 ? [] : [new(row.Record.RecordId, fieldId, catalogId, 1, itemId, "Cero", null, "Zonas", Value: "0", ValueType: "Decimal")]
            }
        }).ToArray();

        var result = await ports.Service.ExportAsync(initial.Id, new(new(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rows = ports.Writer.Request!.Rows;
        Assert.Equal(6, ports.Writer.Request.Headers.Count);
        Assert.Equal("—", rows[0][4].Value);
        Assert.Equal("—", rows[0][5].Value);
        Assert.Null(rows[1][4].Value);
        Assert.Null(rows[1][5].Value);
        Assert.Equal("Cero", rows[2][4].Value);
        Assert.Equal(0m, rows[2][5].Value);
    }

    private static (FormExportService Service, FakeFormWorkbookWriter Writer, FakePermissionService Permissions, FakeFormResponseReader Reader, FakeTimeProvider Clock) Create(bool canExport)
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService(); permissions.Permissions[user.UserId.Value] = canExport
            ? [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport] : [BusinessFormsPermissions.RecordsRead];
        var writer = new FakeFormWorkbookWriter();
        var reader = new FakeFormResponseReader();
        reader.ArrangeExport(10001);
        var clock = new FakeTimeProvider();
        var service = new FormExportService(reader, writer, new(user, permissions),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()), clock, NullLogger<FormExportService>.Instance, new([]));
        return (service, writer, permissions, reader, clock);
    }
}
