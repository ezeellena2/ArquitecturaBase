using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogProposalHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_proposed_catalog_is_inactive_and_searchable_in_either_ui_language_until_human_approval()
    {
        var scenario = await FormCatalogProposalScenario.CreateAsync(factory, "en", Ct);
        var accepted = await scenario.AcceptAsync(Ct);
        using var reviewer = scenario.Client(scenario.ReviewerId);
        reviewer.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        using var detail = await reviewer.GetAsync($"/api/forms/catalogs/{accepted.CatalogId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains("no-store", detail.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
        await FormCatalogProposalContractCapture.WriteAsync(detail, "detail-pending-en", Ct);
        var body = await detail.ReadJsonAsync();
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.False(body.GetProperty("name").TryGetProperty("es", out _));
        Assert.Equal(accepted.Name, body.GetProperty("name").GetProperty("en").GetString());
        var proposal = body.GetProperty("proposal");
        Assert.Equal("Pending", proposal.GetProperty("state").GetString());
        Assert.Equal("en", proposal.GetProperty("originalCulture").GetString());
        Assert.Equal(scenario.RequesterId, proposal.GetProperty("requestedBy").GetProperty("userId").GetGuid());
        Assert.Equal("whatsapp-assistant", proposal.GetProperty("provenance").GetProperty("channelId").GetString());
        Assert.Equal("UnsupportedColumn", Assert.Single(proposal.GetProperty("originalProposal").GetProperty("limitations").EnumerateArray()).GetProperty("code").GetString());
        using var list = await reviewer.GetAsync("/api/forms/catalogs?page=1&pageSize=20&sort=name&search=" + scenario.Suffix, Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        await FormCatalogProposalContractCapture.WriteAsync(list, "list-original-en-in-es", Ct);
        Assert.Equal(accepted.Name, Assert.Single((await list.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("name").GetString());

        var editedName = "Reviewed " + scenario.Suffix;
        var request = new ApproveFormCatalogProposalRequest(1, new Dictionary<string,string?> { ["en"] = editedName },
            new Dictionary<string,string?> { ["en"] = "Reviewed fictitious people" }, ((FormCatalogProposalPayload)scenario.Request.Proposal.Payload).Filters, scenario.RequesterId);
        using var approved = await reviewer.PostAsJsonAsync($"/api/forms/catalogs/{accepted.CatalogId}/proposal/approve", request, Ct);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await FormCatalogProposalContractCapture.WriteAsync(approved, "approved-en", Ct);
        var saved = (await approved.ReadJsonAsync()).GetProperty("catalog");
        Assert.True(saved.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, saved.GetProperty("revision").GetInt64());
        Assert.Equal(editedName, saved.GetProperty("name").GetProperty("en").GetString());
        Assert.Equal("Approved", saved.GetProperty("proposal").GetProperty("state").GetString());
        Assert.Equal(scenario.ReviewerId, saved.GetProperty("proposal").GetProperty("decidedBy").GetProperty("userId").GetGuid());
        Assert.NotEqual(scenario.RequesterId, scenario.ReviewerId);
        Assert.Equal(accepted.Name, saved.GetProperty("proposal").GetProperty("originalProposal").GetProperty("name").GetString());
        using var stale = await reviewer.PostAsJsonAsync($"/api/forms/catalogs/{accepted.CatalogId}/proposal/approve", request, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await FormCatalogProposalContractCapture.WriteAsync(stale, "conflict", Ct);
    }

    [Fact]
    public async Task Concurrent_acceptance_and_replay_after_discard_preserve_one_catalog_and_its_decision()
    {
        var scenario = await FormCatalogProposalScenario.CreateAsync(factory, "es", Ct);
        var accepted = await Task.WhenAll(scenario.AcceptAsync(Ct), scenario.AcceptAsync(Ct));
        Assert.Equal(accepted[0].CatalogId, accepted[1].CatalogId);
        using var reviewer = scenario.Client(scenario.ReviewerId);
        using var detail = await reviewer.GetAsync($"/api/forms/catalogs/{accepted[0].CatalogId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await FormCatalogProposalContractCapture.WriteAsync(detail, "detail-pending-es", Ct);
        using var discarded = await reviewer.PostAsJsonAsync($"/api/forms/catalogs/{accepted[0].CatalogId}/proposal/discard", new DiscardFormCatalogProposalRequest(1), Ct);
        Assert.Equal(HttpStatusCode.OK, discarded.StatusCode);
        await FormCatalogProposalContractCapture.WriteAsync(discarded, "discarded-es", Ct);
        var body = await discarded.ReadJsonAsync();
        Assert.Equal("Discarded", body.GetProperty("proposal").GetProperty("state").GetString());
        Assert.False(body.GetProperty("isActive").GetBoolean());
        var replay = await scenario.AcceptAsync(Ct);
        Assert.Equal(accepted[0].CatalogId, replay.CatalogId);
        Assert.Equal("Discarded", replay.State);
        var rows = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>().AsNoTracking()
            .Where(value => value.ProposalId == scenario.Request.Proposal.Id).Select(value => new { value.Id, value.Revision, value.ProposalEvidenceJson }).ToListAsync(Ct));
        Assert.Equal(2, Assert.Single(rows).Revision);
        Assert.NotNull(rows[0].ProposalEvidenceJson);
    }

    [Fact]
    public async Task A_web_decision_requires_catalog_management_and_never_accepts_sql_or_direct_activation()
    {
        var scenario = await FormCatalogProposalScenario.CreateAsync(factory, "en", Ct);
        var accepted = await scenario.AcceptAsync(Ct);
        using var anonymous = factory.CreateClient();
        using var unsigned = await anonymous.PostAsJsonAsync($"/api/forms/catalogs/{accepted.CatalogId}/proposal/discard", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        var readOnly = await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            var name = "catalog-proposal-reader-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(name, null, [BusinessFormsPermissions.CatalogsRead], Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-proposal-reader")).Value, null, false, "Fictitious reader", "en", Ct);
            await users.SetRolesAsync(user.Id, [name], Ct);
            return user.Id;
        });
        using var reader = scenario.Client(readOnly);
        using var forbidden = await reader.PostAsJsonAsync($"/api/forms/catalogs/{accepted.CatalogId}/proposal/discard", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var manager = scenario.Client(scenario.ReviewerId);
        using var sql = await manager.PostAsJsonAsync($"/api/forms/catalogs/{accepted.CatalogId}/proposal/approve", new { expectedRevision = 1, sql = "SELECT secret" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, sql.StatusCode);
        var filters = ((FormCatalogProposalPayload)scenario.Request.Proposal.Payload).Filters;
        using var activation = await manager.PutAsJsonAsync($"/api/forms/catalogs/{accepted.CatalogId}/filters", new UpdateFiltersCatalogRequest(1,
            new Dictionary<string,string?> { ["en"] = accepted.Name }, new Dictionary<string,string?> { ["en"] = "Fictitious description" }, filters, true), Ct);
        Assert.Equal(HttpStatusCode.Conflict, activation.StatusCode);
        Assert.Equal("Forms.Catalog.Inactive", (await activation.ReadJsonAsync()).GetProperty("code").GetString());
        using var unaltered = await manager.GetAsync($"/api/forms/catalogs/{accepted.CatalogId}", Ct);
        var current = await unaltered.ReadJsonAsync();
        Assert.False(current.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, current.GetProperty("revision").GetInt64());
    }
}
