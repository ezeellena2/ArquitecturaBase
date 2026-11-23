using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    // OT1-C: «Diseñar formularios» sorts in PostgreSQL by every column with a datum. A form never published has no active version
    // and a form nobody changed in the app has no author: both go last in both directions. Equal values keep the usual order of
    // the list, the name. Without «Ver respuestas» the responses keep that order, so it reveals no counts.
    [Fact]
    public async Task Definitions_sort_by_their_columns_with_empty_values_last()
    {
        var token = Guid.NewGuid().ToString("N")[..7];
        var search = "T" + token.ToUpperInvariant();
        using var ana = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.RecordsRead], "Ana " + token);
        using var bruno = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage], "Bruno " + token);
        Guid Identity(char suffix) => Guid.Parse(token + suffix + new string('0', 24));
        // Two published forms, with two responses and with one, seeded without an author; a draft by Bruno and one by Ana that is
        // completed only in the app. By name: two, one, draft, app.
        var two = (await SeedSheetCoreAsync(ana, 2, Identity('1'), [])).Id;
        var one = (await SeedSheetCoreAsync(ana, 1, Identity('2'), [])).Id;
        var draft = (await CreateAsync(bruno, Payload(search + "3 Borrador"))).GetProperty("definition").GetProperty("id").GetGuid();
        var appOnly = Payload(search + "4 Borrador"); appOnly["draft"]!["appOnly"] = true;
        var app = (await CreateAsync(ana, appOnly)).GetProperty("definition").GetProperty("id").GetGuid();
        async Task<Guid[]> Sorted(HttpClient client, string sort)
        {
            using var response = await client.GetAsync($"/api/forms/definitions?search={search}&sort={sort}", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return [.. (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid())];
        }

        // Borrador comes before Publicado in the circuit.
        Assert.Equal([draft, app, two, one], await Sorted(ana, "displayState"));
        Assert.Equal([two, one, draft, app], await Sorted(ana, "-displayState"));
        // «En la app y por WhatsApp» before «Sólo en la app», as the editor offers them.
        Assert.Equal([two, one, draft, app], await Sorted(ana, "completedIn"));
        Assert.Equal([app, two, one, draft], await Sorted(ana, "-completedIn"));
        Assert.Equal([two, one, draft, app], await Sorted(ana, "activeVersion"));
        Assert.Equal([two, one, draft, app], await Sorted(ana, "-activeVersion"));
        Assert.Equal([draft, app, one, two], await Sorted(ana, "responseCount"));
        Assert.Equal([two, one, draft, app], await Sorted(ana, "-responseCount"));
        Assert.Equal([two, one, draft, app], await Sorted(bruno, "responseCount"));
        Assert.Equal([app, draft, two, one], await Sorted(ana, "lastChangedBy"));
        Assert.Equal([draft, app, two, one], await Sorted(ana, "-lastChangedBy"));
    }

    // FU2-A: for whoever can decide approvals, each row counts the responses that await their decision, the same number as
    // «Formularios»; for anyone else the key is not there. «Pending» lists the forms, active or deactivated, with something awaiting
    // the decision of whoever asks, and nothing else; without the permission to decide it is forbidden.
    [Fact]
    public async Task Definitions_count_and_filter_what_awaits_the_decision_of_whoever_asks()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.ApprovalsDecide]);
        using var designer = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.RecordsRead]);
        var active = await SeedApprovalAsync(author, decider, 10);
        var inactive = await SeedApprovalAsync(author, decider, 11);
        // Their own response awaits someone else: nothing for them to decide.
        var own = await SeedApprovalAsync(decider, decider, 12);
        await DeactivateOverviewAsync(inactive.DefinitionId, inactive.DeciderId);
        async Task<JsonElement> Row(HttpClient client, Guid id, string filter = "Active")
        {
            using var response = await client.GetAsync($"/api/forms/definitions?publicationFilter={filter}&search={await OverviewPrefixAsync(id)}", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Assert.Single((await response.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        }

        using var overview = await decider.GetAsync("/api/forms/overview?search=" + await OverviewPrefixAsync(active.DefinitionId), Ct);
        var overviewCount = OverviewRow(await overview.ReadJsonAsync()).GetProperty("pendingApprovalCount").GetInt32();
        Assert.Equal(1, overviewCount);
        Assert.Equal(overviewCount, (await Row(decider, active.DefinitionId)).GetProperty("pendingDecisionCount").GetInt32());
        Assert.Equal(1, (await Row(decider, inactive.DefinitionId, "Deactivated")).GetProperty("pendingDecisionCount").GetInt32());
        Assert.Equal(0, (await Row(decider, own.DefinitionId)).GetProperty("pendingDecisionCount").GetInt32());
        Assert.False((await Row(designer, active.DefinitionId)).TryGetProperty("pendingDecisionCount", out _));

        using var pending = await decider.GetAsync("/api/forms/definitions?publicationFilter=Pending&sort=-pendingDecisionCount", Ct);
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        var pendingJson = await pending.ReadJsonAsync();
        Assert.Equal(2, pendingJson.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { active.DefinitionId, inactive.DefinitionId }.Order(),
            pendingJson.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Order());
        Assert.All(pendingJson.GetProperty("items").EnumerateArray(), row => Assert.Equal(1, row.GetProperty("pendingDecisionCount").GetInt32()));
        using var facets = await decider.GetAsync("/api/forms/definitions/facets?publicationFilter=Pending", Ct);
        Assert.Equal(HttpStatusCode.OK, facets.StatusCode);

        using var forbidden = await designer.GetAsync("/api/forms/definitions?publicationFilter=Pending", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
