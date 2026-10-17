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
