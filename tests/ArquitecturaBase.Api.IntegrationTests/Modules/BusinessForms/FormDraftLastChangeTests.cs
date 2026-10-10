using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publication_does_not_change_the_last_draft_author_date_or_list_order(bool activate)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        using var editor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        using var publisher = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish]);
        var editorId = Guid.Parse(editor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = PublicationHost(clock);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var search = "LAST" + Guid.NewGuid().ToString("N")[..8];
        var saved = await CreateAsync(client, Payload(search + " old"));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        clock.Advance(TimeSpan.FromMinutes(1));
        client.DefaultRequestHeaders.Remove(TestAuthHandler.UserIdHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, editorId.ToString());
        using var edited = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", EditPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        saved = await edited.ReadJsonAsync();
        var lastEditUtc = clock.GetUtcNow().UtcDateTime;
        clock.Advance(TimeSpan.FromMinutes(1));
        var newer = await CreateAsync(client, Payload(search + " newer"));
        var newerId = newer.GetProperty("definition").GetProperty("id").GetGuid();
        clock.Advance(TimeSpan.FromMinutes(1));
        client.DefaultRequestHeaders.Remove(TestAuthHandler.UserIdHeader);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, publisher.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, id, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        if (activate)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            using var reservation = host.Services.CreateScope();
            var work = (await reservation.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>()
                .ReserveAsync((await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), Ct)).Value;
            Assert.NotNull(work);
            var response = new StructuredFormPublicationResponse(work.OperationId, work.DefinitionId, work.VersionId,
                work.Manifest.PublicationFingerprint, "Published", "Verify", [], null, false);
            using var completion = host.Services.CreateScope();
            Assert.True((await completion.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>()
                .CompleteAsync(work, [new(work.Manifest.Profiles[0].ChannelId, response)], Ct)).IsSuccess);
        }

        using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var audit = (await detail.ReadJsonAsync()).GetProperty("audit");
        Assert.Equal(lastEditUtc, audit.GetProperty("lastChangedAtUtc").GetDateTime());
        Assert.Equal(editorId, audit.GetProperty("lastChangedBy").GetProperty("userId").GetGuid());
        Assert.True(audit.GetProperty("modifiedAtUtc").GetDateTime() > lastEditUtc);
        using var list = await client.GetAsync("/api/forms/definitions?search=" + search + "&sort=-lastChangedAtUtc&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(newerId, (await list.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        using var older = await client.GetAsync("/api/forms/definitions?search=" + search + "&sort=lastChangedAtUtc&pageSize=1", Ct);
        var row = (await older.ReadJsonAsync()).GetProperty("items")[0];
        Assert.Equal(id, row.GetProperty("id").GetGuid());
        Assert.Equal(lastEditUtc, row.GetProperty("lastChangedAtUtc").GetDateTime());
        Assert.Equal(editorId, row.GetProperty("lastChangedBy").GetProperty("userId").GetGuid());
    }
}
