using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Sheet_keeps_approval_state_for_the_rule_revision_used_by_a_response()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var sheet = await SeedSheetAsync(reader, 1);
        var route = $"/api/forms/definitions/{sheet.Id}/records";
        using var withoutApproval = await reader.GetAsync(route, Ct);
        Assert.False((await withoutApproval.ReadJsonAsync()).GetProperty("hasApproval").GetBoolean());
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == sheet.Id, Ct);
            var approval = BusinessFormRuleSet.Create(definition.Id, 1, "es", "[]", true, "{\"questions\":[]}");
            var disabled = BusinessFormRuleSet.Create(definition.Id, 2, "es", "[]", false, "{\"questions\":[]}");
            db.AddRange(approval, disabled);
            await db.SaveChangesAsync(Ct);
            definition.SetActiveRuleSetId(disabled.Id);
            var record = await db.Set<BusinessFormRecord>().SingleAsync(item => item.Id == sheet.RecordIds[0], Ct);
            db.Entry(record).Property(item => item.RuleSetId).CurrentValue = approval.Id;
            Assert.True(record.MarkPendingApproval().IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
        foreach (var culture in new[] { "es", "en" })
        {
            reader.DefaultRequestHeaders.AcceptLanguage.Clear();
            reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var historical = await reader.GetAsync(route, Ct);
            Assert.Equal(HttpStatusCode.OK, historical.StatusCode);
            var json = await historical.ReadJsonAsync();
            Assert.True(json.GetProperty("hasApproval").GetBoolean());
            Assert.Equal("PendingApproval", json.GetProperty("records").GetProperty("items")[0].GetProperty("state").GetString());
            await ResponseContractCapture.WriteAsync(historical, "approval-sheet-" + culture, Ct);
        }
        var activeSheet = await SeedSheetAsync(reader, 0);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == activeSheet.Id, Ct);
            var approval = BusinessFormRuleSet.Create(definition.Id, 1, "es", "[]", true, "{\"questions\":[]}");
            db.Add(approval);
            await db.SaveChangesAsync(Ct);
            definition.SetActiveRuleSetId(approval.Id);
            await db.SaveChangesAsync(Ct);
        });
        using var active = await reader.GetAsync($"/api/forms/definitions/{activeSheet.Id}/records", Ct);
        Assert.True((await active.ReadJsonAsync()).GetProperty("hasApproval").GetBoolean());
    }
}
