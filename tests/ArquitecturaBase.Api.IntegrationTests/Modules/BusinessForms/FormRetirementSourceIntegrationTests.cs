using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Retirement_authority_requires_terminal_owned_versions_and_stops_during_any_open_publication()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, author, "es");
        var form = await PublishSuggestionFormAsync(host, client, "Retiro " + Guid.NewGuid().ToString("N")[..8]);
        var other = await PublishSuggestionFormAsync(host, client, "Retiro ajeno " + Guid.NewGuid().ToString("N")[..8]);
        var first = new StructuredFormVersionReference(form.Id, form.VersionId);
        var otherActive = new StructuredFormVersionReference(other.Id, other.VersionId);
        await AssertUnusedVersionsAsync(host, [first, otherActive], []);

        var secondAttempt = await PublicationAttemptAsync(client, form.Id, form.Saved);
        using var secondAccepted = await client.PostAsJsonAsync($"/api/forms/definitions/{form.Id}/publications", secondAttempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, secondAccepted.StatusCode);
        var second = new StructuredFormVersionReference(form.Id, await CompletePublicationAsync(host,
            (await secondAccepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), form.Id));
        await AssertUnusedVersionsAsync(host, [first, second, otherActive], [first]);

        var thirdAttempt = await PublicationAttemptAsync(client, form.Id, form.Saved);
        using var thirdAccepted = await client.PostAsJsonAsync($"/api/forms/definitions/{form.Id}/publications", thirdAttempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, thirdAccepted.StatusCode);
        var thirdReceipt = await thirdAccepted.ReadJsonAsync();
        var third = new StructuredFormVersionReference(form.Id, thirdReceipt.GetProperty("versionId").GetGuid());
        await AssertUnusedVersionsAsync(host, [first, second, third, otherActive], []);
        using (var scope = host.Services.CreateScope())
        {
            var dispatch = scope.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
            var reserved = await dispatch.ReserveAsync(thirdReceipt.GetProperty("operationId").GetGuid(), Ct);
            Assert.True(reserved.IsSuccess);
            var work = Assert.IsType<ArquitecturaBase.Application.Modules.BusinessForms.Models.FormPublicationWorkResponse>(reserved.Value);
            var failed = new StructuredFormPublicationResponse(work.OperationId, work.DefinitionId, work.VersionId,
                work.Manifest.PublicationFingerprint, "Failed", "Publish", [], null, false);
            Assert.True((await dispatch.CompleteAsync(work, [new("test", failed)], Ct)).IsSuccess);
        }
        await AssertUnusedVersionsAsync(host, [first, second, third, otherActive], [first, third]);
        using var deactivated = await client.PostAsJsonAsync($"/api/forms/definitions/{form.Id}/deactivation", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        await AssertUnusedVersionsAsync(host, [first, second, third, otherActive], [first, second, third]);

        var deleted = await PublishSuggestionFormAsync(host, client, "Retiro borrado " + Guid.NewGuid().ToString("N")[..8]);
        var deletedVersion = new StructuredFormVersionReference(deleted.Id, deleted.VersionId);
        using var deletion = await DeleteDefinitionAsync(client, deleted.Id, 1);
        Assert.Equal(HttpStatusCode.NoContent, deletion.StatusCode);
        await AssertUnusedVersionsAsync(host, [deletedVersion], [deletedVersion]);

        await AssertUnusedVersionsAsync(host,
            [new(other.Id, first.VersionId), new(form.Id, other.VersionId), new(Guid.NewGuid(), deleted.VersionId),
                new(form.Id, Guid.NewGuid())], []);
    }

    private static async Task AssertUnusedVersionsAsync(WebApplicationFactory<Program> host,
        IReadOnlyCollection<StructuredFormVersionReference> candidates, IReadOnlyCollection<StructuredFormVersionReference> expected)
    {
        using var scope = host.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IStructuredFormRetirementSource>();
        var result = await source.ListUnusedAsync(candidates, Ct);
        Assert.Equal(expected.OrderBy(value => value.DefinitionId).ThenBy(value => value.VersionId),
            result.OrderBy(value => value.DefinitionId).ThenBy(value => value.VersionId));
    }
}