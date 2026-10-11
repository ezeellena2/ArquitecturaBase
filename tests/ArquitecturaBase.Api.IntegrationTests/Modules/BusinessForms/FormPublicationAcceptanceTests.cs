using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Accepted_publication_seals_fields_version_prefix_and_receipt_and_replays_after_edit_and_expiry()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Durable publication"));
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredFormChannel>(); services.AddSingleton<IStructuredFormChannel, ReadyStructuredFormChannel>();
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        var checkedResult = await check.ReadJsonAsync();
        Assert.True(checkedResult.GetProperty("canAccept").GetBoolean());
        var attempt = CheckPayload(saved); attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString();
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        Assert.Equal("Accepted", receipt.GetProperty("state").GetString());
        Assert.Equal(1, receipt.GetProperty("versionNumber").GetInt64());
        Assert.Null(accepted.Headers.Location);
        var recoveryPath = receipt.GetProperty("recoveryPath").GetString();
        using var state = await client.GetAsync(recoveryPath, Ct);
        Assert.Equal(HttpStatusCode.OK, state.StatusCode);
        Assert.Equal(receipt.GetProperty("operationId").GetGuid(), (await state.ReadJsonAsync()).GetProperty("operationId").GetGuid());
        var definition = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().SingleAsync(x => x.Id == id, Ct));
        Assert.True(definition.PrefixLocked); Assert.Null(definition.ActiveVersionId); Assert.Equal(1, definition.Revision);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().CountAsync(x => x.DefinitionId == id, Ct)));
        Assert.Equal(saved.GetProperty("definition").GetProperty("draft").GetProperty("fields").GetArrayLength(),
            await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormField>().CountAsync(x => x.DefinitionId == id, Ct)));
        var edit = EditPayload(saved); edit["name"]!["es"] = "Nuevo borrador";
        using var edited = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        clock.Advance(TimeSpan.FromMinutes(11));
        using var replay = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(receipt.GetRawText(), (await replay.ReadJsonAsync()).GetRawText());
        attempt["name"]!["es"] = "Otro intento";
        using var mismatch = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Equal("Forms.Publication.PayloadMismatch", (await mismatch.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>().CountAsync(x => x.DefinitionId == id, Ct)));
    }
    [Fact]
    public async Task Publication_manifest_failure_rolls_back_fields_version_operation_and_prefix_lock()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Publication rollback")); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredFormChannel>(); services.AddSingleton<IStructuredFormChannel, ReadyStructuredFormChannel>();
            services.Replace(ServiceDescriptor.Singleton<IFormPublicationProtector, FailingFormPublicationProtector>());
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, id, saved);
        using var failed = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().Where(x => x.Id == id).Select(x => x.PrefixLocked).SingleAsync(Ct)));
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().CountAsync(x => x.DefinitionId == id, Ct)));
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormField>().CountAsync(x => x.DefinitionId == id, Ct)));
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>().CountAsync(x => x.DefinitionId == id, Ct)));
        using var missing = await client.GetAsync($"/api/forms/definitions/{id}/publications/" + attempt["requestId"]!.GetValue<string>(), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Publication_is_owned_by_the_actor_and_requires_read_publish_but_not_manage()
    {
        using var creator = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var saved = await CreateAsync(creator, Payload("Publication permissions")); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = PublicationHost();
        using var publisher = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish]);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, publisher.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, id, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Null(accepted.Headers.Location);
        var recoveryPath = (await accepted.ReadJsonAsync()).GetProperty("recoveryPath").GetString();
        using var other = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish]);
        using var hidden = await other.GetAsync(recoveryPath, Ct);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var anonymous = host.CreateClient();
        using var unauthorized = await anonymous.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        foreach (var codes in new[] { new[] { BusinessFormsPermissions.Read }, new[] { BusinessFormsPermissions.Publish }, new[] { BusinessFormsPermissions.Manage } })
        {
            using var deniedActor = await OperatorAsync(codes);
            using var denied = await deniedActor.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var deniedRead = await deniedActor.GetAsync(recoveryPath, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        }
    }

    [Fact]
    public async Task Fresh_acceptance_rejects_foreign_expired_unsaved_and_stale_checks_without_partial_writes()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Check ownership")); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = PublicationHost(clock);
        using var client = host.CreateClient(); client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, id, saved);
        using var other = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish]);
        using var foreign = await other.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
        Assert.Equal("Forms.Publication.CheckExpired", (await foreign.ReadJsonAsync()).GetProperty("code").GetString());
        var unsaved = attempt.DeepClone().AsObject(); unsaved["name"]!["es"] = "Sin guardar";
        using var different = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", unsaved, Ct);
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal("Forms.Publication.DraftMustBeSaved", (await different.ReadJsonAsync()).GetProperty("code").GetString());
        clock.Advance(TimeSpan.FromMinutes(10));
        using var expired = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
        Assert.Equal("Forms.Publication.CheckExpired", (await expired.ReadJsonAsync()).GetProperty("code").GetString());
        attempt = await PublicationAttemptAsync(client, id, saved);
        using var edited = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", EditPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var stale = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Forms.Draft.RevisionConflict", (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().CountAsync(x => x.DefinitionId == id, Ct)));
    }

    [Fact]
    public async Task Two_hosts_replay_one_operation_and_reject_a_second_open_publication()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Publication concurrency")); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var firstHost = PublicationHost(); await using var secondHost = PublicationHost();
        using var first = firstHost.CreateClient(); using var second = secondHost.CreateClient();
        foreach (var client in new[] { first, second }) client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(first, id, saved);
        var results = await Task.WhenAll(first.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct), second.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct));
        try
        {
            Assert.All(results, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
            Assert.Equal((await results[0].ReadJsonAsync()).GetRawText(), (await results[1].ReadJsonAsync()).GetRawText());
        }
        finally { foreach (var result in results) result.Dispose(); }
        attempt["requestId"] = Guid.NewGuid().ToString();
        using var another = await second.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Conflict, another.StatusCode);
        Assert.Equal("Forms.Publication.InProgress", (await another.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormVersion>().CountAsync(x => x.DefinitionId == id, Ct)));
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> PublicationHost(TimeProvider? clock = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredFormChannel>(); services.AddSingleton<IStructuredFormChannel, ReadyStructuredFormChannel>();
            if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton(clock); }
        }));
    private static async Task<JsonObject> PublicationAttemptAsync(HttpClient client, Guid id, System.Text.Json.JsonElement saved)
    {
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(saved), Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var checkedResult = await check.ReadJsonAsync(); Assert.True(checkedResult.GetProperty("canAccept").GetBoolean());
        var attempt = CheckPayload(saved); attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString(); return attempt;
    }
}
