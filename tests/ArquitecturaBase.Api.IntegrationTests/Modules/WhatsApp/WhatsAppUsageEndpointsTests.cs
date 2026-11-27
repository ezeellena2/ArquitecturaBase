using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Common.Time;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// Consumo de WhatsApp (T7, PR14 to PR16): <c>GET /api/whatsapp/usage?month=</c> asks for «Configuración», says when the installation has no
/// account or number without asking Meta, answers the month at its field, sums what Meta reports (simulated) and says when Meta did not.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppUsageEndpointsTests(ApiFactory factory)
{
    private const string Route = "/api/whatsapp/usage";
    private const string WabaId = "200000000000002";
    private const string DisplayPhoneNumber = "5493406401234";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_route_needs_a_session_and_the_settings_permission()
    {
        var whatsAppOnly = await OperatorAsync(factory, WhatsAppPermissions.Read, WhatsAppPermissions.Send, WhatsAppPermissions.ConfigureAssistant);
        var month = await CurrentMonthAsync(factory);

        using var anonymous = await SendAsync(factory, $"{Route}?month={month}", null);
        using var forbidden = await SendAsync(factory, $"{Route}?month={month}", whatsAppOnly);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Without_the_account_or_the_number_it_says_whatsapp_is_not_configured()
    {
        // The tests' Api has WhatsApp on with neither WhatsApp:WabaId nor WhatsApp:DisplayPhoneNumber, and its Meta client has no network.
        var manager = await OperatorAsync(factory, Permissions.Settings.Manage);
        var month = await CurrentMonthAsync(factory);

        using var response = await SendAsync(factory, $"{Route}?month={month}", manager);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal(["categories", "configured", "currency", "displayPhoneNumber", "month", "totalCost", "totalDelivered"],
            body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(month, body.GetProperty("month").GetString());
        Assert.False(body.GetProperty("configured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("displayPhoneNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("currency").ValueKind);
        Assert.Equal(0, body.GetProperty("categories").GetArrayLength());
        Assert.Equal((0L, 0m), (body.GetProperty("totalDelivered").GetInt64(), body.GetProperty("totalCost").GetDecimal()));
    }

    [Fact]
    public async Task A_month_missing_malformed_or_out_of_range_is_answered_at_its_field()
    {
        var manager = await OperatorAsync(factory, Permissions.Settings.Manage);
        var current = BusinessMonth.TryParse(await CurrentMonthAsync(factory), out var parsed) ? parsed : throw new InvalidOperationException();

        foreach (var (query, code) in new[]
        {
            (Route, WhatsAppUsageErrors.MonthInvalid.Code),
            ($"{Route}?month=2026-13", WhatsAppUsageErrors.MonthInvalid.Code),
            ($"{Route}?month={BusinessMonth.Format(current.AddMonths(1))}", WhatsAppUsageErrors.MonthOutOfRange.Code),
            ($"{Route}?month={BusinessMonth.Format(current.AddMonths(-12))}", WhatsAppUsageErrors.MonthOutOfRange.Code),
        })
        {
            using var response = await SendAsync(factory, query, manager);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.ReadJsonAsync();
            Assert.Equal(code, body.GetProperty("code").GetString());
            Assert.True(body.GetProperty("errors").TryGetProperty("month", out _), query);
        }
    }

    [Fact]
    public async Task With_the_account_and_the_number_it_sums_what_meta_reports_for_the_whole_business_month()
    {
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Json(HttpStatusCode.OK, """
            {"currency":"USD","pricing_analytics":{"data":[{"data_points":[
              {"start":1,"end":2,"country":"AR","pricing_type":"FREE_CUSTOMER_SERVICE","pricing_category":"SERVICE","volume":1300,"cost":0},
              {"start":1,"end":2,"country":"AR","pricing_type":"REGULAR","pricing_category":"MARKETING","volume":70,"cost":4.2},
              {"start":2,"end":3,"country":"AR","pricing_type":"FREE_CUSTOMER_SERVICE","pricing_category":"SERVICE","volume":1180,"cost":0},
              {"start":2,"end":3,"country":"AR","pricing_type":"REGULAR","pricing_category":"MARKETING","volume":50,"cost":3.22}]}]},
             "id":"200000000000002"}
            """));
        await using var host = Configured(meta);
        var manager = await OperatorAsync(host, Permissions.Settings.Manage);
        var current = BusinessMonth.TryParse(await CurrentMonthAsync(host), out var parsed) ? parsed : throw new InvalidOperationException();
        var previous = current.AddMonths(-1);

        using var response = await SendAsync(host, $"{Route}?month={BusinessMonth.Format(previous)}", manager);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var expected = JsonDocument.Parse($$"""
            { "month": "{{BusinessMonth.Format(previous)}}", "configured": true, "displayPhoneNumber": "+54 9 3406 40-1234", "currency": "USD",
              "categories": [ { "category": "MARKETING", "delivered": 120, "cost": 7.42 }, { "category": "SERVICE", "delivered": 2480, "cost": 0 } ],
              "totalDelivered": 2600, "totalCost": 7.42 }
            """);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, await response.ReadJsonAsync()), await response.Content.ReadAsStringAsync(Ct));

        var request = Assert.Single(meta.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"https://graph.facebook.com/v25.0/{WabaId}", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("Bearer test-access-token", request.Authorization);
        var (startUtc, endUtc) = BusinessMonth.Bounds(previous, await ZoneAsync(host));
        Assert.Equal(
            $"?fields=currency,pricing_analytics.start({Unix(startUtc)}).end({Unix(endUtc)}).granularity(DAILY)"
            + $""".phone_numbers(["{DisplayPhoneNumber}"]).dimensions(["PRICING_CATEGORY"])""",
            Uri.UnescapeDataString(request.Uri.Query));
    }

    [Fact]
    public async Task When_meta_does_not_answer_it_says_the_usage_is_unavailable()
    {
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Error(HttpStatusCode.InternalServerError, 2));
        await using var host = Configured(meta);
        var manager = await OperatorAsync(host, Permissions.Settings.Manage);

        using var response = await SendAsync(host, $"{Route}?month={await CurrentMonthAsync(host)}", manager);

        // A provider that does not answer goes as the project's other external failures (Ai.Places.Unavailable): 500 with its code.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal(WhatsAppUsageErrors.Unavailable.Code, body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
        Assert.Equal(1, meta.Calls);
    }

    private WebApplicationFactory<Program> Configured(FakeMetaHandler meta) => factory.WithWebHostBuilder(builder => builder
        .UseSetting("WhatsApp:WabaId", WabaId)
        .UseSetting("WhatsApp:DisplayPhoneNumber", DisplayPhoneNumber)
        .ConfigureTestServices(services =>
            services.AddHttpClient(WhatsAppInfrastructureRegistration.UsageHttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta)));

    private static string Unix(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static async Task<TimeZoneInfo> ZoneAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return BusinessDay.Zone((await scope.ServiceProvider.GetRequiredService<ISystemSettingsReader>().FindPresentationAsync(Ct)).DefaultTimeZoneId);
    }

    private static async Task<string> CurrentMonthAsync(WebApplicationFactory<Program> app) =>
        BusinessMonth.Format(BusinessMonth.Of(app.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime, await ZoneAsync(app)));

    private static async Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> app, string path, Guid? actor)
    {
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (actor is { } id) request.Headers.Add(TestAuthHandler.UserIdHeader, id.ToString());
        return await client.SendAsync(request, Ct);
    }

    private static async Task<Guid> OperatorAsync(WebApplicationFactory<Program> app, params string[] permissions)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var roleName = "usage-" + Guid.NewGuid().ToString("N");
        var userId = Guid.Empty;
        var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            await scope.ServiceProvider.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, permissions, token);
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(roleName + "@example.com").Value, null, false, "Administrador", "es", token);
            await users.SetRolesAsync(user.Id, [roleName], token);
            userId = user.Id;
            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct);
        Assert.True(result.IsSuccess);
        return userId;
    }
}
