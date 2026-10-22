using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Overview_requires_either_permission_and_rejects_private_or_invalid_filters()
    {
        using var anonymous = factory.CreateClient();
        using var unauthorized = await OperatorAsync([BusinessFormsPermissions.Read]);
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var approver = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        using var missing = await anonymous.GetAsync("/api/forms/overview", Ct);
        using var denied = await unauthorized.GetAsync("/api/forms/overview", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var privateFilter = await reader.GetAsync("/api/forms/overview?pendingOnly=true", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, privateFilter.StatusCode);
        foreach (var query in new[] { "?state=All", "?page=0", "?pageSize=101", "?sort=responseCount" })
        {
            using var invalid = await approver.GetAsync("/api/forms/overview" + query, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Overview_counts_decidable_tasks_across_pages_and_includes_inactive_forms_only_when_requested(string culture)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var combined = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.ApprovalsDecide]);
        using var other = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        combined.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        author.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var active = await SeedApprovalAsync(author, combined, 10);
        var inactive = await SeedApprovalAsync(author, combined, 11);
        await SeedApprovalAsync(combined, combined, 12);
        await SeedApprovalAsync(author, other, 13);
        await DeactivateOverviewAsync(inactive.DefinitionId, inactive.DeciderId);
        using var pending = await combined.GetAsync("/api/forms/overview?pendingOnly=true&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.True(pending.Headers.CacheControl?.NoStore);
        var json = await pending.ReadJsonAsync();
        Assert.Equal(2, json.GetProperty("forms").GetProperty("totalCount").GetInt32());
        Assert.Equal(inactive.DefinitionId, OverviewRow(json).GetProperty("id").GetGuid());
        Assert.Equal(1, json.GetProperty("filterCounts").GetProperty("active").GetInt32());
        Assert.Equal(1, json.GetProperty("filterCounts").GetProperty("deactivated").GetInt32());
        Assert.Equal(1, json.GetProperty("filterCounts").GetProperty("deactivatedPending").GetInt32());
        await ResponseContractCapture.WriteAsync(pending, "overview-pending-" + culture, Ct);
        using var second = await combined.GetAsync("/api/forms/overview?pendingOnly=true&pageSize=1&page=2", Ct);
        Assert.Equal(active.DefinitionId, OverviewRow(await second.ReadJsonAsync()).GetProperty("id").GetGuid());
        var activePrefix = await OverviewPrefixAsync(active.DefinitionId);
        using var combinedRow = await combined.GetAsync("/api/forms/overview?search=" + activePrefix, Ct);
        var row = OverviewRow(await combinedRow.ReadJsonAsync());
        Assert.Equal(1, row.GetProperty("pendingApprovalCount").GetInt32());
        Assert.Equal(1, row.GetProperty("responseCount").GetInt64());
        Assert.Equal(active.RecordId, row.GetProperty("lastResponse").GetProperty("id").GetGuid());
        Assert.Equal("PendingApproval", row.GetProperty("lastResponse").GetProperty("state").GetString());
        await ResponseContractCapture.WriteAsync(combinedRow, "overview-combined-" + culture, Ct);
        using var readRow = await author.GetAsync("/api/forms/overview?search=" + activePrefix, Ct);
        var readJson = await readRow.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, OverviewRow(readJson).GetProperty("pendingApprovalCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, readJson.GetProperty("filterCounts").GetProperty("deactivatedPending").ValueKind);
        Assert.Equal(1, OverviewRow(readJson).GetProperty("responseCount").GetInt64());
        await ResponseContractCapture.WriteAsync(readRow, "overview-read-" + culture, Ct);
        using var deactivated = await combined.GetAsync("/api/forms/overview?state=Deactivated&search=" + await OverviewPrefixAsync(inactive.DefinitionId), Ct);
        Assert.Equal(inactive.DefinitionId, OverviewRow(await deactivated.ReadJsonAsync()).GetProperty("id").GetGuid());
        await ResponseContractCapture.WriteAsync(deactivated, "overview-deactivated-" + culture, Ct);
        using var hidden = await combined.GetAsync("/api/forms/overview?search=" + await OverviewPrefixAsync(inactive.DefinitionId), Ct);
        Assert.Empty((await hidden.ReadJsonAsync()).GetProperty("forms").GetProperty("items").EnumerateArray());
        await factory.InTransactionAsync(async services =>
        {
            var role = "overview-revoked-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null,
                [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.ApprovalsDecide], Ct);
            await services.GetRequiredService<IUserRepository>().SetRolesAsync(active.DeciderId, [role], Ct);
        });
        using var revoked = await combined.GetAsync("/api/forms/overview?pendingOnly=true", Ct);
        Assert.Equal(0, (await revoked.ReadJsonAsync()).GetProperty("forms").GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task An_approval_only_overview_keeps_the_state_segment_and_never_exposes_response_metadata(string culture)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var approver = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        approver.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var active = await SeedApprovalAsync(author, approver, 10);
        var inactive = await SeedApprovalAsync(author, approver, 11);
        await SeedApprovalAsync(approver, approver, 12);
        await DeactivateOverviewAsync(inactive.DefinitionId, inactive.DeciderId);
        foreach (var query in new[] { "", "?pendingOnly=true" })
        {
            using var response = await approver.GetAsync("/api/forms/overview" + query, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.ReadJsonAsync();
            var row = OverviewRow(json);
            Assert.Equal(active.DefinitionId, row.GetProperty("id").GetGuid());
            Assert.Equal(1, row.GetProperty("pendingApprovalCount").GetInt32());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("responseCount").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("lastResponse").ValueKind);
            Assert.Equal(1, json.GetProperty("filterCounts").GetProperty("active").GetInt32());
            Assert.Equal(1, json.GetProperty("filterCounts").GetProperty("deactivated").GetInt32());
            if (query.Length == 0) await ResponseContractCapture.WriteAsync(response, "overview-decide-" + culture, Ct);
        }
        using var deactivated = await approver.GetAsync("/api/forms/overview?state=Deactivated", Ct);
        Assert.Equal(inactive.DefinitionId, OverviewRow(await deactivated.ReadJsonAsync()).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task Overview_uses_the_published_content_culture_and_the_record_number_counter(string original, string ui)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(ui);
        var seeded = await SeedSheetCoreAsync(reader, 3, null, [], original, true);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == seeded.Id, Ct);
            var names = JsonSerializer.Serialize(new Dictionary<string, string> { [original] = "Borrador posterior " + seeded.Prefix });
            Assert.True(definition.SetDraft(definition.Revision, "Borrador posterior " + seeded.Prefix, names,
                definition.Prefix, definition.Culture, definition.SubmitLabelJson, definition.SchemaJson, definition.QuestionCount, false).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
        using var response = await reader.GetAsync("/api/forms/overview?search=" + seeded.Prefix, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = OverviewRow(await response.ReadJsonAsync());
        Assert.Equal(seeded.Prefix + (original == "es" ? " Planilla" : " Sheet"), row.GetProperty("name").GetString());
        Assert.Equal(3, row.GetProperty("responseCount").GetInt64());
        Assert.Equal(seeded.RecordIds[^1], row.GetProperty("lastResponse").GetProperty("id").GetGuid());
        Assert.Equal(seeded.Prefix + "-000003", row.GetProperty("lastResponse").GetProperty("number").GetString());
        Assert.Equal("Received", row.GetProperty("lastResponse").GetProperty("state").GetString());
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task Overview_reads_only_the_sealed_presentation_for_search_facets_and_paging_without_a_protector(string original, string ui)
    {
        using var actorClient = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var actor = Guid.Parse(actorClient.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var seeded = await SeedSheetCoreAsync(actorClient, 0, null, [], original, secondaryButtonMissing: true);
        var expected = seeded.Prefix + (original == "es" ? " Planilla" : " Sheet");
        var unpublished = seeded.Prefix + (original == "es" ? " Sheet" : " Planilla");
        await factory.ExecuteScopeAsync(async services =>
        {
            var recorder = new FormResponseCommandRecorder();
            await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(
                services.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
            var overview = new FormOverviewReader(db);

            var page = await overview.FindPageAsync(actor, new ListFormOverviewRequest { Search = expected, PageSize = 1 }, ui, true, false, Ct);

            Assert.Equal(expected, Assert.Single(page.Forms.Items).Name);
            Assert.Equal(1, page.Forms.TotalCount);
            Assert.Equal(1, page.FilterCounts.Active);
            var hidden = await overview.FindPageAsync(actor, new ListFormOverviewRequest { Search = unpublished }, ui, true, false, Ct);
            Assert.Empty(hidden.Forms.Items);
            Assert.Equal(0, hidden.Forms.TotalCount);
            Assert.Equal(0, hidden.FilterCounts.Active);
            Assert.NotEmpty(recorder.Commands);
            Assert.DoesNotContain(recorder.Commands, command => command.Sql.Contains("SchemaJson", StringComparison.Ordinal)
                || command.Sql.Contains("ProtectedManifest", StringComparison.Ordinal));
        });
    }

    private Task<string> OverviewPrefixAsync(Guid id) => factory.ExecuteDbContextAsync(db =>
        db.Set<BusinessFormDefinition>().Where(definition => definition.Id == id).Select(definition => definition.Prefix).SingleAsync(Ct));
    private Task DeactivateOverviewAsync(Guid id, Guid actor) => factory.ExecuteDbContextAsync(async db =>
    {
        var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == id, Ct);
        Assert.True(definition.Deactivate(definition.Revision, actor, factory.Clock.GetUtcNow().UtcDateTime).IsSuccess);
        await db.SaveChangesAsync(Ct);
    });
    private static JsonElement OverviewRow(JsonElement json) => Assert.Single(json.GetProperty("forms").GetProperty("items").EnumerateArray());
}