using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Maximum_valid_form_can_be_retired_without_serializing_a_growing_identity_ledger()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var payload = Payload("Maximum identity ledger");
        var fields = new JsonArray();
        for (var fieldIndex = 0; fieldIndex < 40; fieldIndex++)
        {
            var options = new JsonArray();
            for (var optionIndex = 0; optionIndex < 50; optionIndex++)
                options.Add(new JsonObject { ["localId"] = $"o{optionIndex}", ["label"] = new JsonObject { ["es"] = $"Opción {optionIndex}" } });
            fields.Add(new JsonObject
            {
                ["localId"] = $"f{fieldIndex}", ["type"] = "choice", ["label"] = new JsonObject { ["es"] = $"Lista {fieldIndex}" },
                ["options"] = options,
            });
        }
        payload["draft"]!["fields"] = fields;
        var created = await CreateAsync(actor, payload);
        var definitionId = created.GetProperty("definition").GetProperty("id").GetGuid();
        var edit = EditPayload(created);
        edit["draft"]!["fields"] = new JsonArray();

        using var retired = await actor.PutAsJsonAsync($"/api/forms/definitions/{definitionId}/draft", edit, Ct);

        Assert.Equal(HttpStatusCode.OK, retired.StatusCode);
        var current = await retired.ReadJsonAsync();
        var persistedCount = await factory.ExecuteDbContextAsync(db => db.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM \"BusinessFormRetiredIdentities\" WHERE \"DefinitionId\" = {0}", definitionId)
            .SingleAsync(Ct));
        Assert.Equal(2040, persistedCount);

        using var replay = await actor.PutAsJsonAsync($"/api/forms/definitions/{definitionId}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal((await retired.ReadJsonAsync()).GetProperty("definition").GetProperty("revision").GetInt64(),
            (await replay.ReadJsonAsync()).GetProperty("definition").GetProperty("revision").GetInt64());
        var replayedCount = await factory.ExecuteDbContextAsync(db => db.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM \"BusinessFormRetiredIdentities\" WHERE \"DefinitionId\" = {0}", definitionId)
            .SingleAsync(Ct));
        Assert.Equal(2040, replayedCount);

        var resurrection = EditPayload(current);
        resurrection["draft"]!["fields"] = new JsonArray(new JsonObject
        {
            ["localId"] = "f0", ["fieldId"] = created.GetProperty("identityMap").GetProperty("fields")[0].GetProperty("fieldId").GetGuid().ToString(),
            ["type"] = "choice", ["label"] = new JsonObject { ["es"] = "Lista restaurada" },
            ["options"] = new JsonArray(new JsonObject
            {
                ["localId"] = "o0", ["choiceId"] = created.GetProperty("identityMap").GetProperty("choices")[0].GetProperty("choiceId").GetGuid().ToString(),
                ["label"] = new JsonObject { ["es"] = "Opción restaurada" },
            }),
        });
        using var rejected = await actor.PutAsJsonAsync($"/api/forms/definitions/{definitionId}/draft", resurrection, Ct);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal("Forms.Draft.IdentityRetired", (await rejected.ReadJsonAsync()).GetProperty("code").GetString());
    }
}
