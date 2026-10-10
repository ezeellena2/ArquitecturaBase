using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("Failed", null)]
    [InlineData("Published", null)]
    [InlineData("Published", "Failed")]
    public async Task Publication_check_reports_the_next_number_including_failed_versions_without_allocating_it(string firstState, string? secondState)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Next publication number"));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = PublicationHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var number = 1L;
        foreach (var state in new[] { firstState, secondState }.OfType<string>())
        {
            using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
            Assert.Equal(HttpStatusCode.OK, check.StatusCode);
            var checkedResult = await check.ReadJsonAsync();
            Assert.Equal(number, checkedResult.GetProperty("nextVersionNumber").GetInt64());
            Assert.Equal(number - 1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().LongCountAsync(x => x.DefinitionId == id, Ct)));

            var attempt = CheckPayload(saved);
            attempt["requestId"] = Guid.NewGuid().ToString();
            attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString();
            using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var receipt = await accepted.ReadJsonAsync();
            Assert.Equal(number, receipt.GetProperty("versionNumber").GetInt64());
            using var scope = host.Services.CreateScope();
            var dispatch = scope.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
            var work = (await dispatch.ReserveAsync(receipt.GetProperty("operationId").GetGuid(), Ct)).Value!;
            var response = new StructuredFormPublicationResponse(work.OperationId, id, work.VersionId, work.Manifest.PublicationFingerprint, state, "Verify", [], null, false);
            Assert.True((await dispatch.CompleteAsync(work, [new("test", response)], Ct)).IsSuccess);
            number++;
        }
        using var nextCheck = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, nextCheck.StatusCode);
        Assert.Equal(number, (await nextCheck.ReadJsonAsync()).GetProperty("nextVersionNumber").GetInt64());
        Assert.Equal(number - 1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().LongCountAsync(x => x.DefinitionId == id, Ct)));
        Assert.Equal(number - 1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>().LongCountAsync(x => x.DefinitionId == id, Ct)));
    }
}
