using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    // Optional export contains raw API response bodies for the SPA contract tests, not reconstructed DTOs.
    // Only synthetic actors/content are used. Check tickets, request headers and preview URLs are never exported.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Http_contract_fixtures_preserve_published_versions_and_field_diagnostics(bool rejected)
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var scenario = rejected ? "failed" : "published";
        using var created = await actor.PostAsJsonAsync("/api/forms/definitions", Payload("Contract fixture " + scenario), Ct);
        var saved = await CaptureContractAsync(created, HttpStatusCode.Created, scenario, "created");
        var definition = saved.GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var prefix = definition.GetProperty("prefix").GetString();
        using var unavailableCheck = await actor.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", CheckPayload(saved), Ct);
        var checkedDraft = await CaptureContractAsync(unavailableCheck, HttpStatusCode.OK, scenario, "check-unavailable");
        Assert.False(checkedDraft.GetProperty("canAccept").GetBoolean());
        Assert.Equal(1, checkedDraft.GetProperty("nextVersionNumber").GetInt64());
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        await using var host = PublicationHost(clock);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");

        using var draftList = await client.GetAsync("/api/forms/definitions?search=" + prefix, Ct);
        var draftRows = await CaptureContractAsync(draftList, HttpStatusCode.OK, scenario, "draft-list");
        Assert.Equal("Draft", Assert.Single(draftRows.GetProperty("items").EnumerateArray()).GetProperty("displayState").GetString());
        using var draftDetail = await client.GetAsync($"/api/forms/definitions/{definitionId}", Ct);
        await CaptureContractAsync(draftDetail, HttpStatusCode.OK, scenario, "draft-detail");

        var attempt = await PublicationAttemptAsync(client, definitionId, saved);
        using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);
        var receipt = await CaptureContractAsync(accepted, HttpStatusCode.Accepted, scenario, "accepted");
        var operationId = receipt.GetProperty("operationId").GetGuid();
        var recoveryPath = receipt.GetProperty("recoveryPath").GetString();
        Assert.Equal($"/api/forms/definitions/{definitionId}/publications/{receipt.GetProperty("requestId").GetGuid()}", recoveryPath);
        using var acceptedStatus = await client.GetAsync(recoveryPath, Ct);
        var pending = await CaptureContractAsync(acceptedStatus, HttpStatusCode.OK, scenario, "accepted-status");
        Assert.Equal("Accepted", pending.GetProperty("state").GetString());
        Assert.Empty(pending.GetProperty("issues").EnumerateArray());

        await using (var reservation = host.Services.CreateAsyncScope())
        {
            var service = reservation.ServiceProvider.GetRequiredService<IFormPublicationDispatchService>();
            var work = (await service.ReserveAsync(operationId, Ct)).Value;
            Assert.NotNull(work);
            var response = new StructuredFormPublicationResponse(work.OperationId, work.DefinitionId, work.VersionId,
                work.Manifest.PublicationFingerprint, rejected ? "Failed" : "Published", rejected ? "Upload" : "Verify",
                rejected ? [new StructuredFormIssueResponse(definition.GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid(),
                    null, "fields[0].label.en", FormPublicationErrors.MissingTranslation, Culture: "en")] : [], null, false);
            var channel = Assert.IsType<ReadyStructuredFormChannel>(host.Services.GetRequiredService<IStructuredFormChannel>());
            channel.PublicationResponse = response;
            Assert.True((await service.CompleteAsync(work, [new(channel.ChannelId, response)], Ct)).IsSuccess);
        }

        foreach (var culture in new[] { "es", "en" })
        {
            client.DefaultRequestHeaders.AcceptLanguage.Clear();
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var status = await client.GetAsync(recoveryPath, Ct);
            var outcome = await CaptureContractAsync(status, HttpStatusCode.OK, scenario, "status-" + culture);
            Assert.Equal(rejected ? "Failed" : "Published", outcome.GetProperty("state").GetString());
            if (rejected)
            {
                var issue = Assert.Single(outcome.GetProperty("issues").EnumerateArray());
                Assert.Equal(FormPublicationErrors.MissingTranslation.Code, issue.GetProperty("code").GetString());
                Assert.Equal(definition.GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid(), issue.GetProperty("fieldId").GetGuid());
                Assert.False(string.IsNullOrWhiteSpace(issue.GetProperty("message").GetString()));
            }
            else
            {
                Assert.Empty(outcome.GetProperty("issues").EnumerateArray());
            }
            using var listed = await client.GetAsync("/api/forms/definitions?search=" + prefix, Ct);
            var rows = await CaptureContractAsync(listed, HttpStatusCode.OK, scenario, "list-" + culture);
            var row = Assert.Single(rows.GetProperty("items").EnumerateArray());
            Assert.Equal(rejected ? "Failed" : "Published", row.GetProperty("displayState").GetString());
            if (!rejected) Assert.Equal(1, row.GetProperty("activeVersionNumber").GetInt64());
            using var detail = await client.GetAsync($"/api/forms/definitions/{definitionId}", Ct);
            await CaptureContractAsync(detail, HttpStatusCode.OK, scenario, "detail-" + culture);
        }
        using var nextCheck = await actor.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", CheckPayload(saved), Ct);
        var checkedNext = await CaptureContractAsync(nextCheck, HttpStatusCode.OK, scenario, "check-next-unavailable");
        Assert.False(checkedNext.GetProperty("canAccept").GetBoolean());
        Assert.Equal(2, checkedNext.GetProperty("nextVersionNumber").GetInt64());
    }

    private static async Task<JsonElement> CaptureContractAsync(HttpResponseMessage response, HttpStatusCode expected, string scenario, string name)
    {
        Assert.Equal(expected, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(Ct);
        using var parsed = JsonDocument.Parse(body);
        var json = parsed.RootElement.Clone();
        Assert.True(!json.TryGetProperty("checkToken", out var ticket) || ticket.ValueKind == JsonValueKind.Null);
        var output = Environment.GetEnvironmentVariable("FORMS_HTTP_CONTRACT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var directory = Path.Combine(output, scenario);
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"), body, Ct);
        }
        return json;
    }
}
