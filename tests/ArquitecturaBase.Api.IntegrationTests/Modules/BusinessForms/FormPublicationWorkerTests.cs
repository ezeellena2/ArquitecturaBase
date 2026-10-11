using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormPublicationWorkerTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Publication_worker_queries_the_neutral_channel_and_activates_only_its_matching_version()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Neutral publication worker"));
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var channelDouble = new RetryingStructuredFormChannel { FailuresBeforeAccept = 0 };
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredFormChannel>();
            services.AddSingleton<IStructuredFormChannel>(channelDouble);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, definitionId, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        var operationId = receipt.GetProperty("operationId").GetGuid();

        await host.Services.GetRequiredService<FormPublicationBackgroundService>().ProcessNextAsync(operationId, Ct);

        using var status = await client.GetAsync(accepted.Headers.Location, Ct);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal("Published", (await status.ReadJsonAsync()).GetProperty("state").GetString());
        Assert.Equal(1, channelDouble.AcceptCalls);
        var definition = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == definitionId, Ct));
        Assert.Equal(receipt.GetProperty("versionId").GetGuid(), definition.ActiveVersionId);
    }

    [Fact]
    public async Task Temporary_channel_acceptance_failure_stays_recoverable_and_retries_with_the_same_operation()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var saved = await CreateAsync(actor, Payload("Retry channel acceptance"));
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredFormChannel>();
            services.AddSingleton<RetryingStructuredFormChannel>();
            services.AddSingleton<IStructuredFormChannel>(provider => provider.GetRequiredService<RetryingStructuredFormChannel>());
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var attempt = await PublicationAttemptAsync(client, definitionId, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var receipt = await accepted.ReadJsonAsync();
        var operationId = receipt.GetProperty("operationId").GetGuid();
        var worker = host.Services.GetRequiredService<FormPublicationBackgroundService>();

        await worker.ProcessNextAsync(operationId, Ct);
        using var uncertain = await client.GetAsync(accepted.Headers.Location, Ct);
        Assert.Equal("Uncertain", (await uncertain.ReadJsonAsync()).GetProperty("state").GetString());
        Assert.Equal(1, host.Services.GetRequiredService<RetryingStructuredFormChannel>().AcceptCalls);

        clock.Advance(TimeSpan.FromSeconds(31));
        await worker.ProcessNextAsync(operationId, Ct);
        using var published = await client.GetAsync(accepted.Headers.Location, Ct);
        Assert.Equal("Published", (await published.ReadJsonAsync()).GetProperty("state").GetString());
        Assert.Equal(2, host.Services.GetRequiredService<RetryingStructuredFormChannel>().AcceptCalls);
        var definition = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == definitionId, Ct));
        Assert.Equal(receipt.GetProperty("versionId").GetGuid(), definition.ActiveVersionId);
    }

    [Fact]
    public async Task A_corrupt_due_manifest_does_not_block_another_publication()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var firstSaved = await CreateAsync(actor, Payload("Corrupt first manifest"));
        var firstDefinitionId = firstSaved.GetProperty("definition").GetProperty("id").GetGuid();
        var secondSaved = await CreateAsync(actor, Payload("Later publication can proceed"));
        var secondDefinitionId = secondSaved.GetProperty("definition").GetProperty("id").GetGuid();
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var channelDouble = new RetryingStructuredFormChannel { FailuresBeforeAccept = 0 };
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredFormChannel>();
            services.AddSingleton<IStructuredFormChannel>(channelDouble);
            services.RemoveAll<IFormPublicationProtector>();
            services.AddSingleton(provider => new FailingFormPublicationProtector(new DataProtectionFormPublicationProtector(
                provider.GetRequiredService<IDataProtectionProvider>())));
            services.AddSingleton<IFormPublicationProtector>(provider => provider.GetRequiredService<FailingFormPublicationProtector>());
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var first = await AcceptAsync(client, firstDefinitionId, firstSaved);
        clock.Advance(TimeSpan.FromSeconds(1));
        var second = await AcceptAsync(client, secondDefinitionId, secondSaved);
        host.Services.GetRequiredService<FailingFormPublicationProtector>().FailReadOperationId = first.GetProperty("operationId").GetGuid();

        await host.Services.GetRequiredService<FormPublicationBackgroundService>().ProcessNextAsync(null, Ct);

        var firstOperation = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>()
            .SingleAsync(value => value.Id == first.GetProperty("operationId").GetGuid(), Ct));
        Assert.Equal(FormPublicationState.Uncertain, firstOperation.State);
        Assert.True(firstOperation.NextCheckAtUtc > clock.GetUtcNow().UtcDateTime);
        var secondOperation = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPublicationOperation>()
            .SingleAsync(value => value.Id == second.GetProperty("operationId").GetGuid(), Ct));
        Assert.Equal(FormPublicationState.Published, secondOperation.State);
        Assert.Equal(1, channelDouble.AcceptCalls);
    }

    private async Task<HttpClient> OperatorAsync(IReadOnlyCollection<string> permissions)
    {
        var userId = await factory.InTransactionAsync(async services =>
        {
            var roleName = "publication-worker-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, permissions, Ct);
            var user = await services.GetRequiredService<IUserRepository>().CreateAsync(
                Email.Create(roleName + "@example.com").Value, null, false, "Operador", "es", Ct);
            await services.GetRequiredService<IUserRepository>().SetRolesAsync(user.Id, [roleName], Ct);
            return user.Id;
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return client;
    }

    private static JsonObject Payload(string name) => new()
    {
        ["requestId"] = Guid.NewGuid().ToString(),
        ["name"] = new JsonObject { ["es"] = name, ["en"] = name },
        ["prefix"] = "T" + Guid.NewGuid().ToString("N")[..10],
        ["draft"] = new JsonObject
        {
            ["format"] = 1,
            ["culture"] = "es",
            ["submitLabel"] = new JsonObject { ["es"] = "Enviar", ["en"] = "Submit" },
            ["fields"] = new JsonArray(new JsonObject
            {
                ["localId"] = "first", ["type"] = "shortText",
                ["label"] = new JsonObject { ["es"] = "Nombre", ["en"] = "Name" },
            }),
        },
    };

    private static async Task<System.Text.Json.JsonElement> CreateAsync(HttpClient client, JsonObject payload)
    {
        using var response = await client.PostAsJsonAsync("/api/forms/definitions", payload, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static async Task<JsonObject> PublicationAttemptAsync(HttpClient client, Guid id, System.Text.Json.JsonElement saved)
    {
        var checkPayload = CheckPayload(saved);
        using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", checkPayload, Ct);
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var checkedResult = await check.ReadJsonAsync();
        Assert.True(checkedResult.GetProperty("canAccept").GetBoolean());
        var attempt = CheckPayload(saved);
        attempt["requestId"] = Guid.NewGuid().ToString();
        attempt["checkToken"] = checkedResult.GetProperty("checkToken").GetString();
        return attempt;
    }

    private static async Task<System.Text.Json.JsonElement> AcceptAsync(HttpClient client, Guid definitionId, System.Text.Json.JsonElement saved)
    {
        var attempt = await PublicationAttemptAsync(client, definitionId, saved);
        using var response = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static JsonObject CheckPayload(System.Text.Json.JsonElement saved)
    {
        var definition = saved.GetProperty("definition");
        var payload = JsonNode.Parse(definition.GetRawText())!.AsObject();
        payload.Remove("id"); payload.Remove("prefixLocked"); payload.Remove("revision");
        payload.Remove("requestId");
        payload["expectedRevision"] = definition.GetProperty("revision").GetInt64();
        var fields = payload["draft"]!["fields"]!.AsArray();
        for (var index = 0; index < fields.Count; index++)
        {
            fields[index]!["localId"] = "f" + index;
            fields[index]!.AsObject().Remove("key");
            fields[index]!.AsObject().Remove("replacesFieldId");
            if (fields[index]!["options"] is JsonArray options)
                for (var optionIndex = 0; optionIndex < options.Count; optionIndex++) options[optionIndex]!["localId"] = "o" + optionIndex;
        }
        return payload;
    }
}
