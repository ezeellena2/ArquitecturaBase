using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Version_readers_execute_as_server_queries()
    {
        using var scope = factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence.IFormPublicationReader>();
        Assert.Null(await reader.FindActiveVersionAsync(Guid.NewGuid(), Ct));
        Assert.Null(await reader.FindVersionAsync(Guid.NewGuid(), Guid.NewGuid(), Ct));
    }
    [Fact]
    public async Task Activation_uses_the_sealed_version_and_keeps_a_later_draft_edit_and_rejects_a_late_fence()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Sealed activation")); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = PublicationHost(); using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, id, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var reservation = host.Services.CreateScope();
        var work = (await reservation.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>().ReserveAsync((await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), Ct)).Value;
        Assert.NotNull(work);
        var edit = EditPayload(saved); edit["name"]!["es"] = "Borrador posterior";
        using var edited = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct); Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var response = new StructuredFormPublicationResponse(work.OperationId, work.DefinitionId, work.VersionId, work.Manifest.PublicationFingerprint, "Published", "Verify", [], null, false);
        using var completion = host.Services.CreateScope();
        Assert.True((await completion.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>().CompleteAsync(work, [new(work.Manifest.Profiles[0].ChannelId, response)], Ct)).IsSuccess);
        var definition = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == id, Ct));
        Assert.Equal(work.VersionId, definition.ActiveVersionId); Assert.Equal(2, definition.Revision); Assert.Equal("Borrador posterior", definition.Name);
        using var late = host.Services.CreateScope();
        Assert.True((await late.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>().CompleteAsync(work, [new(work.Manifest.Profiles[0].ChannelId, response)], Ct)).IsSuccess);
        using var obsolete = host.Services.CreateScope();
        Assert.True((await obsolete.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>().CompleteAsync(work with { Fence = work.Fence + 1 }, [new(work.Manifest.Profiles[0].ChannelId, response)], Ct)).IsFailure);
    }
}
