using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task Publishing_can_add_and_remove_a_secondary_language_in_new_sealed_versions(string original, string secondary)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var payload = Payload("Publication languages");
        payload["name"] = new System.Text.Json.Nodes.JsonObject { [original] = "Original" };
        payload["draft"]!["culture"] = original;
        var saved = await CreateAsync(actor, payload);
        var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var host = PublicationHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));

        var first = await PublishAsync(saved, 1, [original], [original], []);
        var edit = EditPayload(saved);
        edit["name"]![secondary] = "Translation";
        using var translated = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, translated.StatusCode);
        saved = await translated.ReadJsonAsync();

        var second = await PublishAsync(saved, 2, ["en", "es"], [secondary], []);
        edit = EditPayload(saved);
        edit["draft"]!["submitLabel"]![secondary] = "";
        using var incomplete = await client.PutAsJsonAsync($"/api/forms/definitions/{id}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, incomplete.StatusCode);
        saved = await incomplete.ReadJsonAsync();

        var third = await PublishAsync(saved, 3, [original], [], [secondary]);

        using var persistedScope = host.Services.CreateScope();
        var reader = persistedScope.ServiceProvider.GetRequiredService<IFormPublicationReader>();
        var protector = persistedScope.ServiceProvider.GetRequiredService<IFormPublicationProtector>();
        foreach (var (publication, expected) in new[] { (first, new[] { original }), (second, new[] { "en", "es" }), (third, new[] { original }) })
        {
            var stored = Assert.IsType<FormPublicationStoredRow>(await reader.FindVersionAsync(id, publication.VersionId, Ct));
            var manifest = protector.ReadManifest(new(stored.Id, stored.Actor, stored.RequestId, id, stored.VersionId), stored.ProtectedManifest);
            Assert.Equal(expected, manifest.Profiles.Single().Cultures.Select(culture => culture.Culture).Order(StringComparer.Ordinal));
        }
        Assert.NotEqual(first.VersionId, second.VersionId);
        Assert.NotEqual(second.VersionId, third.VersionId);

        async Task<FormPublicationWorkResponse> PublishAsync(JsonElement draft, int number, string[] expectedCultures, string[] addedCultures, string[] removedCultures)
        {
            // The unavailable host returns the same language report without an acceptance ticket.
            using var exportableCheck = await actor.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(draft), Ct);
            var capturedCheck = await CaptureContractAsync(exportableCheck, HttpStatusCode.OK, Path.Combine("languages", original), $"v{number}-check");
            Assert.Equal(expectedCultures, capturedCheck.GetProperty("channels")[0].GetProperty("cultures").EnumerateArray()
                .Select(value => value.GetString()).Order(StringComparer.Ordinal));
            Assert.Equal(addedCultures, capturedCheck.GetProperty("diff").GetProperty("addedCultures").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(removedCultures, capturedCheck.GetProperty("diff").GetProperty("removedCultures").EnumerateArray().Select(value => value.GetString()));
            using var check = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publication-checks", CheckPayload(draft), Ct);
            Assert.Equal(HttpStatusCode.OK, check.StatusCode);
            var result = await check.ReadJsonAsync();
            Assert.True(result.GetProperty("canAccept").GetBoolean());
            Assert.Equal("LocallyValidated", result.GetProperty("compatibility").GetString());
            Assert.Empty(result.GetProperty("issues").EnumerateArray());
            Assert.Equal(expectedCultures, result.GetProperty("channels")[0].GetProperty("cultures").EnumerateArray()
                .Select(value => value.GetString()).Order(StringComparer.Ordinal));
            var secondaryReport = Assert.Single(result.GetProperty("cultures").EnumerateArray(), culture => culture.GetProperty("culture").GetString() == secondary);
            Assert.False(secondaryReport.GetProperty("isOriginal").GetBoolean());
            Assert.Equal(expectedCultures.Contains(secondary), secondaryReport.GetProperty("isComplete").GetBoolean());
            Assert.Equal(addedCultures, result.GetProperty("diff").GetProperty("addedCultures").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(removedCultures, result.GetProperty("diff").GetProperty("removedCultures").EnumerateArray().Select(value => value.GetString()));
            var attempt = CheckPayload(draft);
            attempt["requestId"] = Guid.NewGuid().ToString();
            attempt["checkToken"] = result.GetProperty("checkToken").GetString();
            using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            var receipt = await accepted.ReadJsonAsync();
            using var reservation = host.Services.CreateScope();
            var service = reservation.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
            var reserved = await service.ReserveAsync(receipt.GetProperty("operationId").GetGuid(), Ct);
            Assert.True(reserved.IsSuccess);
            var work = Assert.IsType<FormPublicationWorkResponse>(reserved.Value);
            Assert.Equal(expectedCultures, work.Manifest.Profiles.Single().Cultures.Select(culture => culture.Culture).Order(StringComparer.Ordinal));
            Assert.Equal(expectedCultures, work.ChannelRequests.Single().Request.Snapshot.TargetCultures);
            var publication = new StructuredFormPublicationResponse(work.OperationId, id, work.VersionId,
                work.Manifest.PublicationFingerprint, "Published", "Verify", [], null, false);
            Assert.IsType<ReadyStructuredFormChannel>(host.Services.GetRequiredService<IStructuredFormChannel>()).PublicationResponse = publication;
            Assert.True((await service.CompleteAsync(work, [new(work.Manifest.Profiles.Single().ChannelId, publication)], Ct)).IsSuccess);
            using var status = await client.GetAsync(receipt.GetProperty("recoveryPath").GetString(), Ct);
            var capturedStatus = await CaptureContractAsync(status, HttpStatusCode.OK, Path.Combine("languages", original), $"v{number}-status");
            Assert.Equal("Published", capturedStatus.GetProperty("state").GetString());
            Assert.Equal(work.VersionId, capturedStatus.GetProperty("versionId").GetGuid());
            Assert.Equal(expectedCultures, capturedStatus.GetProperty("cultures").EnumerateArray()
                .Select(value => value.GetString()).Order(StringComparer.Ordinal));
            using var detail = await client.GetAsync($"/api/forms/definitions/{id}", Ct);
            var capturedDetail = await CaptureContractAsync(detail, HttpStatusCode.OK, Path.Combine("languages", original), $"v{number}-detail");
            var active = capturedDetail.GetProperty("publication").GetProperty("activeVersion");
            Assert.Equal(work.VersionId, active.GetProperty("versionId").GetGuid());
            Assert.Equal(number, active.GetProperty("versionNumber").GetInt64());
            Assert.Equal(expectedCultures, active.GetProperty("cultures").EnumerateArray()
                .Select(value => value.GetString()).Order(StringComparer.Ordinal));
            return work;
        }
    }
}
