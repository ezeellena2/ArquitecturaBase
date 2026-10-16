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
    [Fact]
    public async Task Export_requires_its_own_permission_before_reading_responses()
    {
        var ports = Create(false);
        Assert.True((await ports.Service.ExportAsync(Guid.NewGuid(), new(new(), null), CancellationToken.None)).IsFailure);
        Assert.Equal(0, ports.Responses.Calls);
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
        ports.Responses.Sheet = ports.Responses.Sheet with { Records = new([], 1, 100, 0) };
        ports.Writer.BeforeReturn = () => ports.Permissions.Permissions.Clear();
        var result = await ports.Service.ExportAsync(Guid.NewGuid(), new(new(), []), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }
    private static (FormExportService Service, FakeFormExportResponses Responses, FakeFormWorkbookWriter Writer, FakePermissionService Permissions) Create(bool canExport)
    {
        var user = new FakeCurrentUser { UserId = Guid.NewGuid() };
        var permissions = new FakePermissionService(); permissions.Permissions[user.UserId.Value] = canExport
            ? [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport] : [BusinessFormsPermissions.RecordsRead];
        var responses = new FakeFormExportResponses(); var writer = new FakeFormWorkbookWriter();
        var service = new FormExportService(responses, new FakeFormResponseReader(), writer, new(user, permissions),
            RequestValidators.For(new FormExportRequestValidator(), new ListFormRecordsRequestValidator()), new FakeTimeProvider(), NullLogger<FormExportService>.Instance);
        return (service, responses, writer, permissions);
    }
}
