using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Draft_recovery_and_publication_recovery_paths_remain_in_the_existing_http_bodies()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        using var created = await actor.PostAsJsonAsync("/api/forms/definitions", Payload("Recovery path contract"), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync();
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var draftRequestId = saved.GetProperty("requestId").GetGuid();
        var draftRecoveryPath = saved.GetProperty("recovery").GetProperty("path").GetString();
        Assert.Equal($"/api/forms/draft-changes/{draftRequestId}", draftRecoveryPath);
        using var recoveredDraft = await actor.GetAsync(draftRecoveryPath, Ct);
        Assert.Equal(HttpStatusCode.OK, recoveredDraft.StatusCode);
        Assert.Equal(draftRecoveryPath, (await recoveredDraft.ReadJsonAsync()).GetProperty("result").GetProperty("recovery").GetProperty("path").GetString());

        await using var host = PublicationHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, definitionId, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Null(accepted.Headers.Location);
        var receipt = await accepted.ReadJsonAsync();
        var publicationRecoveryPath = receipt.GetProperty("recoveryPath").GetString();
        using var detail = await client.GetAsync($"/api/forms/definitions/{definitionId}", Ct);
        Assert.Equal(publicationRecoveryPath, (await detail.ReadJsonAsync()).GetProperty("publication")
            .GetProperty("currentOperation").GetProperty("recoveryPath").GetString());
    }
}
