using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Local_deprecation_status_has_the_same_contract_in_list_editor_and_deactivation_without_record_read(string culture)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var channel = new ControlledFormChannelStateSource();
        await using var publicationHost = PublicationHost(clock);
        await using var host = publicationHost.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredFormChannelStateSource>();
            services.AddSingleton<IStructuredFormChannelStateSource>(channel);
        }));
        using var client = DeactivationClient(host, author, culture);
        var name = "ESTADO" + Guid.NewGuid().ToString("N")[..8];
        var form = await PublishSuggestionFormAsync(host, client, name);
        using var deactivation = await client.PostAsJsonAsync($"/api/forms/definitions/{form.Id}/deactivation", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivation.StatusCode);
        foreach (var state in new[] { StructuredFormDeprecationState.Pending, StructuredFormDeprecationState.Uncertain, StructuredFormDeprecationState.NeedsAttention, StructuredFormDeprecationState.Deprecated })
        {
            var at = clock.GetUtcNow().UtcDateTime;
            var expiry = state == StructuredFormDeprecationState.Pending ? at.AddHours(1) : (DateTime?)null;
            var confirmedAt = state == StructuredFormDeprecationState.Deprecated ? at : (DateTime?)null;
            channel.Rows = [new(form.Id, expiry, false) { DeprecationState = state, DeprecatedAtUtc = confirmedAt }];
            using var detail = await client.GetAsync($"/api/forms/definitions/{form.Id}", Ct);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            AssertRetirementState((await detail.ReadJsonAsync()).GetProperty("lifecycle"), state, expiry, confirmedAt);
            await FormLifecycleContractCapture.WriteAsync(detail, culture, "status-" + state.ToString().ToLowerInvariant(), Ct);
            using var list = await client.GetAsync($"/api/forms/definitions?publicationFilter=Deactivated&search={name}", Ct);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var row = Assert.Single((await list.ReadJsonAsync()).GetProperty("items").EnumerateArray());
            Assert.Equal(form.Id, row.GetProperty("id").GetGuid());
            AssertRetirementState(row.GetProperty("lifecycle"), state, expiry, confirmedAt);
            using var preview = await client.GetAsync($"/api/forms/definitions/{form.Id}/deactivation", Ct);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            AssertRetirementState((await preview.ReadJsonAsync()).GetProperty("lifecycle"), state, expiry, confirmedAt);
        }
    }

    private static void AssertRetirementState(JsonElement lifecycle, StructuredFormDeprecationState state, DateTime? expiry, DateTime? confirmedAt)
    {
        Assert.Equal(state.ToString(), lifecycle.GetProperty("deprecationState").GetString());
        if (expiry is { } expires) Assert.Equal(expires, lifecycle.GetProperty("lastOpenSessionExpiresAtUtc").GetDateTime());
        else Assert.Equal(JsonValueKind.Null, lifecycle.GetProperty("lastOpenSessionExpiresAtUtc").ValueKind);
        if (confirmedAt is { } confirmed) Assert.Equal(confirmed, lifecycle.GetProperty("deprecatedAtUtc").GetDateTime());
        else Assert.Equal(JsonValueKind.Null, lifecycle.GetProperty("deprecatedAtUtc").ValueKind);
        AssertDeactivationRecordsHidden(lifecycle);
    }
}