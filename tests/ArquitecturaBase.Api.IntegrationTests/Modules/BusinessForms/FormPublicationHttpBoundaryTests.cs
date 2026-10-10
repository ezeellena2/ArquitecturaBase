using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Publication_http_rejects_unknown_members_before_accepting_a_version()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Closed publication request"));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = PublicationHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, id, saved);
        attempt["unknownSetting"] = true;

        using var response = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Request.Invalid", (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().CountAsync(version => version.DefinitionId == id, Ct)));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(definition => definition.Id == id).Select(definition => definition.PrefixLocked).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Publication_http_preview_declares_conflict_as_problem_details()
    {
        var document = await OpenApiTests.ReadDocumentAsync(factory);
        var preview = document.GetProperty("paths").GetProperty("/api/forms/definitions/{id}/versions/{versionId}/preview").GetProperty("get");

        Assert.Equal("#/components/schemas/ProblemDetails", preview.GetProperty("responses").GetProperty("409")
            .GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").GetProperty("$ref").GetString());
    }
}
