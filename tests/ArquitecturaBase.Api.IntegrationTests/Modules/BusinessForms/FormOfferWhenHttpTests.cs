using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Offer_instructions_save_recover_and_clear_with_the_draft_without_publishing()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var payload = Payload("Offer instructions");
        payload["offerWhen"] = "  Cuando cuenta una visita.  ";
        var saved = await CreateAsync(client, payload);
        var definition = saved.GetProperty("definition");
        var id = definition.GetProperty("id").GetGuid();
        Assert.Equal("Cuando cuenta una visita.", definition.GetProperty("offerWhen").GetString());
        var schema = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(value => value.Id == id).Select(value => value.SchemaJson).SingleAsync(Ct));
        Assert.DoesNotContain("Cuando", schema, StringComparison.Ordinal);
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().CountAsync(value => value.DefinitionId == id, Ct)));
        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        Assert.Equal(definition.GetRawText(), (await detail.ReadJsonAsync()).GetProperty("definition").GetRawText());
        using var recovered = await client.GetAsync("/api/forms/draft-changes/" + saved.GetProperty("requestId").GetGuid(), Ct);
        Assert.Equal(saved.GetRawText(), (await recovered.ReadJsonAsync()).GetProperty("result").GetRawText());

        payload["offerWhen"] = "Cuando cuenta una visita.";
        using var replay = await client.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        Assert.Equal(saved.GetRawText(), (await replay.ReadJsonAsync()).GetRawText());
        payload["offerWhen"] = "Otra instrucción";
        using var changedReplay = await client.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        Assert.Equal(HttpStatusCode.Conflict, changedReplay.StatusCode);

        var update = EditPayload(saved);
        update["offerWhen"] = "   ";
        using var cleared = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", update, Ct);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var result = await cleared.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, result.GetProperty("definition").GetProperty("offerWhen").ValueKind);
        Assert.Equal(2, result.GetProperty("definition").GetProperty("revision").GetInt64());
        Assert.Equal(schema, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(value => value.Id == id).Select(value => value.SchemaJson).SingleAsync(Ct)));

        var stale = EditPayload(saved);
        stale["offerWhen"] = "No debe guardarse";
        using var conflict = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", stale, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Null(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(value => value.Id == id).Select(value => value.OfferWhen).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Offer_instructions_are_monolingual_bounded_and_excluded_from_publication_requests()
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var payload = Payload("Offer original only");
        payload["offerWhen"] = "Cuando cuenta una visita.";
        var saved = await CreateAsync(client, payload);
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var invalid = EditPayload(saved);
        invalid["offerWhen"] = new string('a', 1001);
        using var oversized = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", invalid, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.True((await oversized.ReadJsonAsync()).GetProperty("errors").TryGetProperty("offerWhen", out _));
        var check = CheckPayload(saved);
        check["offerWhen"] = "No es parte de la publicación";
        using var unknown = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", check, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        check.Remove("offerWhen");
        using var valid = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", check, Ct);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal("LocallyValidated", (await valid.ReadJsonAsync()).GetProperty("compatibility").GetString());
    }
}
