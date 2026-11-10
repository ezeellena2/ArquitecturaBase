using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// RE1-A with PostgreSQL: the result of an external action is kept once per run in the response, sealed with its names, and the
// detail and the sheet show it in the display language; a second result for the same run is refused by the unique index.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task The_result_of_an_external_action_is_kept_once_per_run_and_read_in_the_detail_and_the_sheet()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedWorkflowRunAsync(reader, FormActionRunState.Completed);
        var result = new StructuredRecordResult("arca.invoice",
            new Dictionary<string, string> { ["es"] = "Emitir comprobante en ARCA", ["en"] = "Issue voucher in ARCA" },
            [
                new("voucher", new Dictionary<string, string> { ["es"] = "Comprobante", ["en"] = "Voucher" }, StructuredRecordResultField.Text, "Factura C 0004-00000012"),
                new("authorization", new Dictionary<string, string> { ["es"] = "Código de autorización" }, StructuredRecordResultField.Text, "74512345678901"),
                new("expiresOn", new Dictionary<string, string> { ["es"] = "Vencimiento del código" }, StructuredRecordResultField.Date, "2026-11-19"),
            ]);
        var definitionId = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormRecord>().Where(record => record.Id == seeded.RecordId)
            .Select(record => record.DefinitionId).SingleAsync(Ct));
        async Task AddAsync() => await factory.ExecuteScopeAsync(async services =>
        {
            services.GetRequiredService<FormRecordResultRecorder>().Add(definitionId, seeded.RecordId, seeded.RunId, result);
            await services.GetRequiredService<ApplicationDbContext>().SaveChangesAsync(Ct);
        });

        await AddAsync();
        await Assert.ThrowsAsync<DbUpdateException>(AddAsync);

        using var detail = await reader.GetAsync($"/api/forms/records/{seeded.RecordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var stored = Assert.Single((await detail.ReadJsonAsync()).GetProperty("results").EnumerateArray());
        Assert.Equal("arca.invoice", stored.GetProperty("source").GetString());
        Assert.Equal("Emitir comprobante en ARCA", stored.GetProperty("name").GetString());
        Assert.Equal(["Comprobante", "Código de autorización", "Vencimiento del código"],
            stored.GetProperty("values").EnumerateArray().Select(value => value.GetProperty("label").GetString()));
        Assert.Equal("2026-11-19", stored.GetProperty("values")[2].GetProperty("value").GetString());
        Assert.Equal("date", stored.GetProperty("values")[2].GetProperty("kind").GetString());

        // The sheet takes its columns from the latest result of each source and fills the row that has one.
        using var sheet = await reader.GetAsync($"/api/forms/definitions/{definitionId}/records", Ct);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        var body = await sheet.ReadJsonAsync();
        var columns = body.GetProperty("resultColumns").EnumerateArray().ToArray();
        Assert.Equal(["Comprobante", "Código de autorización", "Vencimiento del código"], columns.Select(column => column.GetProperty("label").GetString()));
        var row = body.GetProperty("records").GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == seeded.RecordId);
        Assert.Equal("Factura C 0004-00000012", row.GetProperty("results").GetProperty(columns[0].GetProperty("id").GetString()!).GetString());
    }

    [Fact]
    public async Task A_response_without_results_keeps_its_detail_body()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedWorkflowRunAsync(reader, FormActionRunState.Completed);
        using var detail = await reader.GetAsync($"/api/forms/records/{seeded.RecordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.False((await detail.ReadJsonAsync()).TryGetProperty("results", out _));
    }
}
