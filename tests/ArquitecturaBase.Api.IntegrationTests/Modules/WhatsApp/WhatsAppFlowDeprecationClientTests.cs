using System.Net;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFlowDeprecationClientTests
{
    [Fact]
    public async Task Deprecation_posts_only_to_the_fixed_flow_and_requires_a_confirmed_acknowledgement()
    {
        using var handler = new StubFlowHttpHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true}") });
        using var http = new HttpClient(handler) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var settings = Settings();
        var result = await new WhatsAppFlowClient(http, Options.Create(settings)).DeprecateAsync(Account(settings), "333", TestContext.Current.CancellationToken);
        Assert.True(result.Success); Assert.False(result.Uncertain);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v25.0/333/deprecate", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
        Assert.Equal("", Assert.Single(handler.Bodies));
    }

    [Theory]
    [InlineData(503, "{}", true)]
    [InlineData(429, "{}", true)]
    [InlineData(200, "{}", true)]
    [InlineData(200, "{", true)]
    [InlineData(200, "[]", true)]
    [InlineData(200, "null", true)]
    [InlineData(400, "{}", false)]
    public async Task Deprecation_preserves_unknown_results_but_distinguishes_a_known_rejection(int code, string json, bool uncertain)
    {
        using var handler = new StubFlowHttpHandler(_ => new((HttpStatusCode)code) { Content = new StringContent(json) });
        using var http = new HttpClient(handler) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var settings = Settings();
        var result = await new WhatsAppFlowClient(http, Options.Create(settings)).DeprecateAsync(Account(settings), "333", TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.Equal(uncertain, result.Uncertain);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("333", "fixed", "222", "PUBLISHED", true, WhatsAppFlowRemoteState.Published)]
    [InlineData("333", "fixed", "222", "DEPRECATED", true, WhatsAppFlowRemoteState.Deprecated)]
    [InlineData("333", "fixed", "222", "DRAFT", true, WhatsAppFlowRemoteState.Unknown)]
    [InlineData("999", "fixed", "222", "DEPRECATED", false, WhatsAppFlowRemoteState.Unknown)]
    [InlineData("333", "other", "222", "DEPRECATED", false, WhatsAppFlowRemoteState.Unknown)]
    [InlineData("333", "fixed", "999", "DEPRECATED", false, WhatsAppFlowRemoteState.Unknown)]
    public async Task State_reads_verify_identity_and_owner_without_requiring_assets_or_send_health(
        string id, string name, string owner, string status, bool identityMatches, WhatsAppFlowRemoteState expected)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { id, name, status, whatsapp_business_account = new { id = owner } });
        using var handler = new StubFlowHttpHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(json) });
        using var http = new HttpClient(handler) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var settings = Settings();
        var result = await new WhatsAppFlowClient(http, Options.Create(settings)).GetStateAsync(Account(settings), "333", "fixed", TestContext.Current.CancellationToken);
        Assert.Equal(identityMatches, result.IdentityMatches); Assert.Equal(expected, result.State);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/v25.0/333", request.RequestUri!.AbsolutePath);
        var query = Uri.UnescapeDataString(request.RequestUri.Query);
        Assert.Contains("status", query, StringComparison.Ordinal);
        Assert.Contains("whatsapp_business_account", query, StringComparison.Ordinal);
        Assert.DoesNotContain("health_status", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_different_connection_never_posts_or_adopts_a_remote_flow()
    {
        using var handler = new StubFlowHttpHandler(_ => throw new InvalidOperationException("The different connection must not be called."));
        using var http = new HttpClient(handler) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var settings = Settings();
        var client = new WhatsAppFlowClient(http, Options.Create(settings));
        var other = new WhatsAppFlowAccountContext("999", "111", "v25.0", "old");
        Assert.False((await client.DeprecateAsync(other, "333", TestContext.Current.CancellationToken)).Success);
        var state = await client.GetStateAsync(other, "333", "fixed", TestContext.Current.CancellationToken);
        Assert.False(state.IdentityMatches); Assert.Equal(WhatsAppFlowRemoteState.Unknown, state.State);
        Assert.Empty(handler.Requests);
    }


    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task A_rejected_deprecation_is_classified_for_a_later_state_read(int code)
    {
        using var handler = new StubFlowHttpHandler(request => request.Method == HttpMethod.Post
            ? new((HttpStatusCode)code) { Content = new StringContent("{}") }
            : new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"333\",\"name\":\"fixed\",\"status\":\"DEPRECATED\",\"whatsapp_business_account\":{\"id\":\"222\"}}") });
        using var http = new HttpClient(handler) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var settings = Settings();
        var client = new WhatsAppFlowClient(http, Options.Create(settings));
        var result = await client.DeprecateAsync(Account(settings), "333", TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.False(result.Uncertain);
        Assert.Equal("WhatsApp.Flow.DeprecationRejected", result.FailureCode);
        var state = await client.GetStateAsync(Account(settings), "333", "fixed", TestContext.Current.CancellationToken);
        Assert.True(state.IdentityMatches);
        Assert.Equal(WhatsAppFlowRemoteState.Deprecated, state.State);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("/v25.0/333", handler.Requests[1].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Publication_rejections_keep_the_existing_effect_contract()
    {
        using var handler = new StubFlowHttpHandler(_ => new(HttpStatusCode.BadRequest) { Content = new StringContent("{}") });
        using var http = new HttpClient(handler) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var settings = Settings();
        var result = await new WhatsAppFlowClient(http, Options.Create(settings)).PublishAsync(Account(settings), "333", TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.False(result.Uncertain); Assert.Null(result.FailureCode);
    }

    private static WhatsAppOptions Settings() => new() { WabaId = "222", PhoneNumberId = "111", AccessToken = "test-token" };
    private static WhatsAppFlowAccountContext Account(WhatsAppOptions settings) => new(settings.WabaId!, settings.PhoneNumberId!, settings.GraphApiVersion, "configuration");
}
