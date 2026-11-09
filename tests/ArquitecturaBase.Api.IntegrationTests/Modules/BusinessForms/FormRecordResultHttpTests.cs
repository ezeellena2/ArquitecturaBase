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
// detail shows it in the display language; a second result for the same run is refused by the unique index.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task The_result_of_an_external_action_is_kept_once_per_run_and_read_in_the_detail()
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
        async Task AddAsync() => await factory.ExecuteScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var definitionId = await db.Set<BusinessFormRecord>().Where(record => record.Id == seeded.RecordId).Select(record => record.DefinitionId).SingleAsync(Ct);
            services.GetRequiredService<FormRecordResultRecorder>().Add(definitionId, seeded.RecordId, seeded.RunId, result);
            await db.SaveChangesAsync(Ct);
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
