using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormAssistantProposalHttpTests(ApiFactory factory)
{
    private const string Definitions = "/api/forms/definitions/assistant-proposals";
    private const string Catalogs = "/api/forms/catalogs/assistant-proposals";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_form_proposal_creates_its_draft_once_and_keeps_where_it_came_from()
    {
        await using var scenario = await FormAssistantProposalScenario.CreateAsync(factory, Ct);
        var request = scenario.Form("Control " + scenario.Suffix, scenario.Prefix);
        using var author = scenario.Client(scenario.AuthorId);
        using var created = await author.PostAsJsonAsync(Definitions, request, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"{Definitions}/{request.ProposalId}", created.Headers.Location!.AbsolutePath);
        await FormAssistantProposalContractCapture.WriteAsync(created, "draft-created", Ct);
        var definitionId = (await created.ReadJsonAsync()).GetProperty("definitionId").GetGuid();

        using var again = await author.PostAsJsonAsync(Definitions, request, Ct);
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(definitionId, (await again.ReadJsonAsync()).GetProperty("definitionId").GetGuid());
        Assert.Equal(1, scenario.Lookup.Reads);
        using var status = await author.GetAsync($"{Definitions}/{request.ProposalId}", Ct);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Contains("no-store", status.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
        await FormAssistantProposalContractCapture.WriteAsync(status, "draft-status-created", Ct);
        Assert.Equal(definitionId, (await status.ReadJsonAsync()).GetProperty("definitionId").GetGuid());

        using var detail = await author.GetAsync($"/api/forms/definitions/{definitionId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var definition = (await detail.ReadJsonAsync()).GetProperty("definition");
        Assert.Equal(("Control " + scenario.Suffix, scenario.Prefix), (definition.GetProperty("name").GetProperty("es").GetString(), definition.GetProperty("prefix").GetString()));
        Assert.Equal(["shortText", "decimal"], definition.GetProperty("draft").GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("type").GetString()));
        var stored = await scenario.DefinitionAsync(definitionId, Ct);
        Assert.Equal(request.ProposalId, stored.ProposalId);
        using var evidence = JsonDocument.Parse(stored.ProposalEvidenceJson!);
        Assert.Equal(("app", scenario.AuthorId), (evidence.RootElement.GetProperty("requestedIn").GetString(), evidence.RootElement.GetProperty("requestedByUserId").GetGuid()));

        using var reader = scenario.Client(scenario.ReaderId);
        using var foreign = await reader.GetAsync($"{Definitions}/{request.ProposalId}", Ct);
        Assert.Equal(HttpStatusCode.OK, foreign.StatusCode);
        await FormAssistantProposalContractCapture.WriteAsync(foreign, "draft-status-not-created", Ct);
        Assert.Equal(JsonValueKind.Null, (await foreign.ReadJsonAsync()).GetProperty("definitionId").ValueKind);
    }

    [Fact]
    public async Task Two_taps_at_once_create_one_draft()
    {
        await using var scenario = await FormAssistantProposalScenario.CreateAsync(factory, Ct);
        var request = scenario.Form("Doble " + scenario.Suffix, scenario.Prefix);
        using var first = scenario.Client(scenario.AuthorId);
        using var second = scenario.Client(scenario.AuthorId);
        var responses = await Task.WhenAll(first.PostAsJsonAsync(Definitions, request, Ct), second.PostAsJsonAsync(Definitions, request, Ct));
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            var ids = await Task.WhenAll(responses.Select(async response => (await response.ReadJsonAsync()).GetProperty("definitionId").GetGuid()));
            Assert.Equal(ids[0], ids[1]);
            Assert.Equal(1, await scenario.CountDefinitionsAsync(request.ProposalId!.Value, Ct));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task A_name_or_prefix_in_use_or_a_replaced_or_unknown_proposal_creates_nothing()
    {
        await using var scenario = await FormAssistantProposalScenario.CreateAsync(factory, Ct);
        using var author = scenario.Client(scenario.AuthorId);
        var name = "Usado " + scenario.Suffix;
        var other = "OT" + scenario.Suffix[..6].ToUpperInvariant();
        using (var first = await author.PostAsJsonAsync(Definitions, scenario.Form(name, scenario.Prefix), Ct))
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await AssertProblemAsync(author.PostAsJsonAsync(Definitions, scenario.Form(name.ToUpperInvariant(), other), Ct),
            HttpStatusCode.BadRequest, "Forms.Definition.NameTaken", "draft-name-taken");
        await AssertProblemAsync(author.PostAsJsonAsync(Definitions, scenario.Form("Otro " + scenario.Suffix, scenario.Prefix), Ct),
            HttpStatusCode.BadRequest, "Forms.Definition.PrefixTaken", "draft-prefix-taken");
        await AssertProblemAsync(author.PostAsJsonAsync(Definitions, scenario.Form("Vieja " + scenario.Suffix, other, current: false), Ct),
            HttpStatusCode.Conflict, "Forms.Definition.ProposalReplaced", "draft-replaced");
        await AssertProblemAsync(author.PostAsJsonAsync(Definitions, new CreateFromAssistantProposalRequest(Guid.NewGuid(), Guid.NewGuid()), Ct),
            HttpStatusCode.NotFound, "Forms.Definition.ProposalUnavailable", "draft-unavailable");
        await AssertProblemAsync(author.PostAsJsonAsync(Definitions, new { requestId = Guid.NewGuid() }, Ct),
            HttpStatusCode.BadRequest, "Forms.Draft.ContentInvalid", "draft-invalid");
    }

    private static readonly string[] VersionOneShape = ["code", "culture", "description", "fields", "name", "option", "valueType"];

    // Stage 2: version 2 creates the same catalog; its evidence keeps the turn's request and version 1's shape, never the spreadsheet.
    [Fact]
    public async Task A_catalog_proposal_with_a_spreadsheet_creates_the_same_catalog_without_keeping_the_spreadsheet()
    {
        await using var scenario = await FormAssistantProposalScenario.CreateAsync(factory, Ct);
        var request = scenario.MappedCatalog("Fleteros " + scenario.Suffix);
        using var author = scenario.Client(scenario.AuthorId);
        using var created = await author.PostAsJsonAsync(Catalogs, request, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogId = (await created.ReadJsonAsync()).GetProperty("catalogId").GetGuid();
        var catalog = await scenario.CatalogAsync(catalogId, Ct);
        Assert.Equal((BusinessFormCatalogKind.Manual, false, BusinessFormCatalogProposalState.Pending), (catalog.Kind, catalog.IsActive, catalog.ProposalState));
        using var evidence = JsonDocument.Parse(catalog.ProposalEvidenceJson!);
        Assert.Equal((request.RequestId, 2), (evidence.RootElement.GetProperty("requestId").GetGuid(), evidence.RootElement.GetProperty("capabilityVersion").GetInt32()));
        // PostgreSQL keeps jsonb keys in its own order: what matters is which ones.
        Assert.Equal(VersionOneShape, evidence.RootElement.GetProperty("manual").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("transportistas.xlsx", catalog.ProposalEvidenceJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("Transportes Sur", catalog.ProposalEvidenceJson!, StringComparison.Ordinal);

        using var detail = await author.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        var proposal = (await detail.ReadJsonAsync()).GetProperty("proposal");
        Assert.Equal(VersionOneShape, proposal.GetProperty("manual").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(proposal.TryGetProperty("requestId", out _));
    }

    [Fact]
    public async Task A_catalog_proposal_creates_an_inactive_pending_catalog_that_approving_activates()
    {
        await using var scenario = await FormAssistantProposalScenario.CreateAsync(factory, Ct);
        var name = "Transportistas " + scenario.Suffix;
        var request = scenario.Catalog(name);
        using var author = scenario.Client(scenario.AuthorId);
        using var created = await author.PostAsJsonAsync(Catalogs, request, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal($"{Catalogs}/{request.ProposalId}", created.Headers.Location!.AbsolutePath);
        await FormAssistantProposalContractCapture.WriteAsync(created, "catalog-created", Ct);
        var catalogId = (await created.ReadJsonAsync()).GetProperty("catalogId").GetGuid();
        using var status = await author.GetAsync($"{Catalogs}/{request.ProposalId}", Ct);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        await FormAssistantProposalContractCapture.WriteAsync(status, "catalog-status-created", Ct);
        Assert.Equal(catalogId, (await status.ReadJsonAsync()).GetProperty("catalogId").GetGuid());

        using var detail = await author.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await FormAssistantProposalContractCapture.WriteAsync(detail, "catalog-detail-pending", Ct);
        var body = await detail.ReadJsonAsync();
        Assert.Equal(("Manual", false, "Text"), (body.GetProperty("kind").GetString(), body.GetProperty("isActive").GetBoolean(), body.GetProperty("valueType").GetString()));
        Assert.Equal(["razon_social", "telefono"], body.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("key").GetString()));
        var proposal = body.GetProperty("proposal");
        Assert.Equal(("Pending", "app", "home"), (proposal.GetProperty("state").GetString(), proposal.GetProperty("requestedIn").GetString(),
            proposal.GetProperty("provenance").GetProperty("channelId").GetString()));
        Assert.Equal(("CUIT", "Razón social"), (proposal.GetProperty("manual").GetProperty("code").GetString(), proposal.GetProperty("manual").GetProperty("option").GetString()));
        Assert.False(proposal.TryGetProperty("originalProposal", out _));

        using var approved = await author.PostAsJsonAsync($"/api/forms/catalogs/{catalogId}/proposal/approve", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await FormAssistantProposalContractCapture.WriteAsync(approved, "catalog-approved", Ct);
        var approval = await approved.ReadJsonAsync();
        Assert.Empty(approval.GetProperty("fieldIdentityMap").EnumerateObject());
        var saved = approval.GetProperty("catalog");
        Assert.Equal((true, 2L, "Approved"), (saved.GetProperty("isActive").GetBoolean(), saved.GetProperty("revision").GetInt64(),
            saved.GetProperty("proposal").GetProperty("state").GetString()));
        Assert.Equal(scenario.AuthorId, saved.GetProperty("proposal").GetProperty("decidedBy").GetProperty("userId").GetGuid());
        Assert.Equal(2, saved.GetProperty("fields").GetArrayLength());

        using var again = await author.PostAsJsonAsync(Catalogs, request, Ct);
        Assert.Equal(catalogId, (await again.ReadJsonAsync()).GetProperty("catalogId").GetGuid());
        await AssertProblemAsync(author.PostAsJsonAsync(Catalogs, scenario.Catalog(name), Ct), HttpStatusCode.BadRequest, "Forms.Catalog.NameTaken", "catalog-name-taken");
        await AssertProblemAsync(author.PostAsJsonAsync(Catalogs, scenario.Catalog("Otro " + scenario.Suffix, current: false), Ct),
            HttpStatusCode.Conflict, "Forms.Catalog.ProposalReplaced", "catalog-replaced");
    }

    [Fact]
    public async Task The_routes_need_a_session_and_their_permissions()
    {
        await using var scenario = await FormAssistantProposalScenario.CreateAsync(factory, Ct);
        var form = scenario.Form("Permisos " + scenario.Suffix, scenario.Prefix);
        var catalog = scenario.Catalog("Permisos " + scenario.Suffix);
        using var anonymous = scenario.AnonymousClient();
        using var reader = scenario.Client(scenario.ReaderId);
        using var outsider = scenario.Client(scenario.OutsiderId);
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (reader, HttpStatusCode.Forbidden) })
        {
            using var definitions = await client.PostAsJsonAsync(Definitions, form, Ct);
            using var catalogs = await client.PostAsJsonAsync(Catalogs, catalog, Ct);
            Assert.Equal((status, status), (definitions.StatusCode, catalogs.StatusCode));
        }
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (outsider, HttpStatusCode.Forbidden) })
        {
            using var definitions = await client.GetAsync($"{Definitions}/{form.ProposalId}", Ct);
            using var catalogs = await client.GetAsync($"{Catalogs}/{catalog.ProposalId}", Ct);
            Assert.Equal((status, status), (definitions.StatusCode, catalogs.StatusCode));
        }
        Assert.Equal(0, scenario.Lookup.Reads);
    }

    private static async Task AssertProblemAsync(Task<HttpResponseMessage> sending, HttpStatusCode status, string code, string capture)
    {
        using var response = await sending;
        Assert.Equal(status, response.StatusCode);
        await FormAssistantProposalContractCapture.WriteAsync(response, capture, Ct);
        Assert.Equal(code, (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }
}
