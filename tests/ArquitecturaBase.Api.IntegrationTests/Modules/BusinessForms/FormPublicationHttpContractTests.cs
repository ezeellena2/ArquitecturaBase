using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Publication_acceptance_returns_202_without_location_and_keeps_recovery_path_in_the_body()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Acceptance HTTP contract"));
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = PublicationHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, definitionId, saved);

        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Null(accepted.Headers.Location);
        var receipt = await accepted.ReadJsonAsync();
        var requestId = receipt.GetProperty("requestId").GetGuid();
        var recoveryPath = receipt.GetProperty("recoveryPath").GetString();
        Assert.Equal($"/api/forms/definitions/{definitionId}/publications/{requestId}", recoveryPath);
        using var status = await client.GetAsync(recoveryPath, Ct);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }
}
