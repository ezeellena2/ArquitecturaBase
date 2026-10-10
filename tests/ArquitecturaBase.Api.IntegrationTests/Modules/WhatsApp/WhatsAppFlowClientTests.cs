using System.Net;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFlowClientTests
{
    [Fact]
    public async Task Phone_membership_lookup_distinguishes_provider_unavailability_from_a_confirmed_mismatch()
    {
        using var unavailable = new StubFlowHttpHandler(_ => new(HttpStatusCode.ServiceUnavailable)
        { Content = new StringContent("{}") });
        using var unavailableHttp = new HttpClient(unavailable) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var unavailableResult = await new WhatsAppFlowClient(unavailableHttp, Options.Create(Settings()))
            .VerifyPhoneAsync("222", "111", TestContext.Current.CancellationToken);
        Assert.True(unavailableResult.IsFailure);

        using var confirmed = new StubFlowHttpHandler(_ => new(HttpStatusCode.OK)
        { Content = new StringContent("{\"data\":[{\"id\":\"999\"}]}") });
        using var confirmedHttp = new HttpClient(confirmed) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var confirmedResult = await new WhatsAppFlowClient(confirmedHttp, Options.Create(Settings()))
            .VerifyPhoneAsync("222", "111", TestContext.Current.CancellationToken);
        Assert.True(confirmedResult.IsSuccess);
        Assert.False(confirmedResult.Value);
    }

    [Fact]
    public async Task Successful_upload_keeps_validation_pointers_but_discards_private_provider_messages()
    {
        using var handler = new StubFlowHttpHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(
            "{\"success\":true,\"validation_errors\":[{\"message\":\"PRIVATE_PROVIDER_TEXT\",\"pointers\":[{\"path\":\"screens[0].layout.children[0].children[2].data-source[1].title\"}]}]}") });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var settings = Settings(); var provider = new WhatsAppFlowClient(http, Options.Create(settings));
        var result = await provider.UploadAsync(Account(settings), "333", "{\"private\":true}", TestContext.Current.CancellationToken);
        Assert.True(result.Success); Assert.False(result.Uncertain);
        var issue = Assert.Single(result.ValidationErrors); Assert.Equal(2, issue.FieldIndex); Assert.Equal(1, issue.ChoiceIndex);
        Assert.DoesNotContain("PRIVATE_PROVIDER_TEXT", result.ToString(), StringComparison.Ordinal);
        Assert.Equal("/v25.0/333/assets", Assert.Single(handler.Requests).RequestUri!.AbsolutePath);
    }
    [Theory]
    [InlineData("screens[0].layout.children[0].children[0]['max-chars']", 0, null, "max-chars")]
    [InlineData("screens[0].layout.children[0].children[1]['max-length']", 1, null, "max-length")]
    [InlineData("screens[0].layout.children[0].children[2]['max-chars']", 2, null, "max-chars")]
    [InlineData("screens[0].layout.children[0].children[2]['data-source'][1]['title']", 2, 1, "title")]
    [InlineData("screens[0].layout.children[0].children[2].data-source[1].title", 2, 1, "title")]
    [InlineData("screens[1].layout.children[0].children[2]['max-chars']", null, null, "channel")]
    [InlineData("screens[0].layout.children[0].children[2]['PRIVATE_PROPERTY']", null, null, "channel")]
    public async Task Provider_bracket_paths_preserve_safe_field_and_choice_indices_without_private_messages(
        string path, int? fieldIndex, int? choiceIndex, string property)
    {
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            success = true,
            validation_errors = new[] { new { message = "PRIVATE_PROVIDER_TEXT", pointers = new[] { new { path } } } },
        });
        using var handler = new StubFlowHttpHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(body) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var settings = Settings(); var provider = new WhatsAppFlowClient(http, Options.Create(settings));
        var result = await provider.UploadAsync(Account(settings), "333", "{}", TestContext.Current.CancellationToken);
        var issue = Assert.Single(result.ValidationErrors);
        Assert.Equal(fieldIndex, issue.FieldIndex); Assert.Equal(choiceIndex, issue.ChoiceIndex); Assert.Equal(property, issue.Property);
        Assert.DoesNotContain("PRIVATE_PROVIDER_TEXT", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_PROPERTY", result.ToString(), StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }
    [Fact]
    public async Task Create_success_without_id_and_gateway_failure_are_uncertain_single_attempts()
    {
        foreach (var response in new[] { HttpStatusCode.OK, HttpStatusCode.BadGateway })
        {
            using var handler = new StubFlowHttpHandler(_ => new(response) { Content = new StringContent("{\"success\":true}") });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
            var settings = Settings(); var provider = new WhatsAppFlowClient(http, Options.Create(settings));
            var result = await provider.CreateAsync(Account(settings), "form_operation_es", TestContext.Current.CancellationToken);
            Assert.True(result.Uncertain); Assert.Null(result.MetaFlowId); Assert.Single(handler.Requests);
        }
    }
    [Fact]
    public async Task Official_preview_preserves_the_exact_expiry_and_rejects_an_arbitrary_origin()
    {
        foreach (var origin in new[] { "https://business.facebook.com/wa/manage/flows/333/preview/?token=private", "https://evil.example/preview?token=private" })
        {
            using var handler = new StubFlowHttpHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(
                "{\"id\":\"333\",\"preview\":{\"preview_url\":\"" + origin + "\",\"expires_at\":\"2026-10-11T13:00:00+0000\"}}") });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
            var settings = Settings(); var provider = new WhatsAppFlowClient(http, Options.Create(settings));
            var result = await provider.GetPreviewAsync(Account(settings), "333", TestContext.Current.CancellationToken);
            Assert.Equal(origin.StartsWith("https://business.facebook.com/", StringComparison.Ordinal), result.IsSuccess);
            Assert.Contains("invalidate(false)", Assert.Single(handler.Requests).RequestUri!.ToString(), StringComparison.Ordinal);
            if (result.IsSuccess)
            {
                Assert.Equal(new DateTime(2026, 10, 11, 13, 0, 0, DateTimeKind.Utc), result.Value.ExpiresAtUtc);
                Assert.DoesNotContain("token=private", result.Value.ToString(), StringComparison.Ordinal);
            }
        }
    }
    [Fact]
    public async Task Unknown_creation_reads_account_candidates_without_adopting_or_recreating_them()
    {
        using var handler = new StubFlowHttpHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"333\",\"name\":\"technical_name\"},{\"id\":\"444\",\"name\":\"technical_name\"}]}") });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var settings = Settings(); var provider = new WhatsAppFlowClient(http, Options.Create(settings));
        var result = await provider.InspectUnknownCreationAsync(Account(settings), "technical_name", TestContext.Current.CancellationToken);
        Assert.Equal(2, result.CreationCandidateCount); Assert.False(result.Published); Assert.False(result.AssetAndOwnerMatch);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }
    [Theory]
    [InlineData("222", "https://scontent.xx.fbcdn.net/flow.json", true)]
    [InlineData("222", "https://mmg.whatsapp.net/flow.json", true)]
    [InlineData("999", "https://scontent.xx.fbcdn.net/flow.json", false)]
    [InlineData("222", "https://evil.example/flow.json", false)]
    [InlineData("222", "https://other.whatsapp.net/flow.json", false)]
    [InlineData("222", "https://mmg.whatsapp.net.evil.example/flow.json", false)]
    [InlineData("222", "http://mmg.whatsapp.net/flow.json", false)]
    [InlineData("222", "https://mmg.whatsapp.net:444/flow.json", false)]
    [InlineData("222", "https://private@mmg.whatsapp.net/flow.json", false)]
    public async Task Verification_requires_the_expected_owner_and_exact_asset_and_never_sends_the_bearer_to_the_asset_host(string owner, string assetUrl, bool expected)
    {
        const string json = "{\"version\":\"7.3\"}";
        using var handler = new StubFlowHttpHandler(request =>
        {
            if (request.RequestUri!.Host != "graph.facebook.com")
            {
                Assert.Null(request.Headers.Authorization); return new(HttpStatusCode.OK) { Content = new StringContent(json) };
            }
            var content = request.RequestUri.AbsolutePath == "/v25.0/111"
                ? "{\"id\":\"111\",\"health_status\":{\"can_send_message\":\"AVAILABLE\"}}"
                : request.RequestUri.AbsolutePath.EndsWith("/assets", StringComparison.Ordinal)
                ? "{\"data\":[{\"name\":\"flow.json\",\"asset_type\":\"FLOW_JSON\",\"download_url\":\"" + assetUrl + "\"}]}"
                : "{\"id\":\"333\",\"name\":\"technical_name\",\"status\":\"PUBLISHED\",\"json_version\":\"7.3\",\"validation_errors\":[],\"whatsapp_business_account\":{\"id\":\"" + owner + "\"},\"health_status\":{\"can_send_message\":\"AVAILABLE\"}}";
            Assert.NotNull(request.Headers.Authorization); return new(HttpStatusCode.OK) { Content = new StringContent(content) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var settings = Settings(); var provider = new WhatsAppFlowClient(http, Options.Create(settings));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        var result = await provider.VerifyAsync(Account(settings), "333", "technical_name", hash, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.AssetAndOwnerMatch);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.Host == "evil.example");
    }
    [Theory]
    [InlineData("AVAILABLE", "AVAILABLE", "111", true)]
    [InlineData("BLOCKED", "AVAILABLE", "111", false)]
    [InlineData("AVAILABLE", "BLOCKED", "111", false)]
    [InlineData(null, "AVAILABLE", "111", false)]
    [InlineData("AVAILABLE", null, "111", false)]
    [InlineData("AVAILABLE", "AVAILABLE", "999", false)]
    public async Task Verification_reads_flow_and_exact_phone_health_without_unsupported_field_parameters(
        string? flowHealth, string? phoneHealth, string phoneId, bool healthy)
    {
        const string json = "{\"version\":\"7.3\"}";
        using var handler = new StubFlowHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri!.Host == "mmg.whatsapp.net")
            {
                Assert.Null(request.Headers.Authorization);
                return new(HttpStatusCode.OK) { Content = new StringContent(json) };
            }
            Assert.NotNull(request.Headers.Authorization);
            var fields = Uri.UnescapeDataString(request.RequestUri.Query);
            Assert.DoesNotContain("phone_number(", fields, StringComparison.Ordinal);
            var content = request.RequestUri.AbsolutePath switch
            {
                "/v25.0/333" => System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = "333", name = "technical_name", status = "DRAFT", json_version = "7.3",
                    validation_errors = Array.Empty<object>(), whatsapp_business_account = new { id = "222" },
                    health_status = new { can_send_message = flowHealth },
                }),
                "/v25.0/111" => System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = phoneId, health_status = new { can_send_message = phoneHealth },
                }),
                "/v25.0/333/assets" => "{\"data\":[{\"name\":\"flow.json\",\"asset_type\":\"FLOW_JSON\",\"download_url\":\"https://mmg.whatsapp.net/flow.json\"}]}",
                _ => throw new InvalidOperationException("Unexpected verification request."),
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(content) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var settings = Settings(); var provider = new WhatsAppFlowClient(http, Options.Create(settings));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        var result = await provider.VerifyAsync(Account(settings), "333", "technical_name", hash, TestContext.Current.CancellationToken);
        Assert.True(result.Draft); Assert.False(result.Published); Assert.True(result.Validated);
        Assert.True(result.AssetAndOwnerMatch); Assert.Equal(healthy, result.Healthy);
        Assert.Single(handler.Requests, request => request.RequestUri!.AbsolutePath == "/v25.0/111");
    }
    [Theory]
    [InlineData("wrong-hash")]
    [InlineData("redirect")]
    [InlineData("oversized")]
    [InlineData("duplicate")]
    public async Task Verification_rejects_wrong_bytes_redirects_oversized_bodies_and_ambiguous_assets(string scenario)
    {
        const string json = "{\"version\":\"7.3\"}";
        const string asset = "{\"name\":\"flow.json\",\"asset_type\":\"FLOW_JSON\",\"download_url\":\"https://mmg.whatsapp.net/flow.json\"}";
        using var handler = new StubFlowHttpHandler(request =>
        {
            if (request.RequestUri!.Host == "mmg.whatsapp.net")
            {
                Assert.Null(request.Headers.Authorization);
                if (scenario == "redirect")
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri("https://evil.example/private"); return redirect;
                }
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(scenario == "oversized" ? new string('x', 1024 * 1024 + 1) : json),
                };
            }
            Assert.Equal("graph.facebook.com", request.RequestUri.Host);
            var content = request.RequestUri.AbsolutePath switch
            {
                "/v25.0/333" => "{\"id\":\"333\",\"name\":\"technical_name\",\"status\":\"PUBLISHED\",\"json_version\":\"7.3\",\"validation_errors\":[],\"whatsapp_business_account\":{\"id\":\"222\"},\"health_status\":{\"can_send_message\":\"AVAILABLE\"}}",
                "/v25.0/111" => "{\"id\":\"111\",\"health_status\":{\"can_send_message\":\"AVAILABLE\"}}",
                "/v25.0/333/assets" => "{\"data\":[" + asset + (scenario == "duplicate" ? "," + asset : "") + "]}",
                _ => throw new InvalidOperationException("Unexpected verification request."),
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(content) };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var settings = Settings(); var provider = new WhatsAppFlowClient(http, Options.Create(settings));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(scenario == "wrong-hash" ? "{}" : json)));
        var result = await provider.VerifyAsync(Account(settings), "333", "technical_name", hash, TestContext.Current.CancellationToken);
        Assert.False(result.AssetAndOwnerMatch); Assert.True(result.Healthy);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.Host == "evil.example");
        if (scenario == "duplicate") Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.Host == "mmg.whatsapp.net");
    }
    private static WhatsAppOptions Settings() => new() { PhoneNumberId = "111", WabaId = "222", AccessToken = "test-token", GraphApiVersion = "v25.0" };
    private static WhatsAppFlowAccountContext Account(WhatsAppOptions settings) => new("222", "111", "v25.0", WhatsAppFlowEnvironment.Fingerprint(settings));
}
