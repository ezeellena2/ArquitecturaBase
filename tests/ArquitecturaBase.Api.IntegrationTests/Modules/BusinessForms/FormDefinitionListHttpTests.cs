using System.Net;
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
}
