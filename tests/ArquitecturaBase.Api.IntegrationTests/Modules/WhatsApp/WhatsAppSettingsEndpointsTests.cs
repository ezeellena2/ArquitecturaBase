using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// Configuración › WhatsApp (RC3-A): the settings read the values of before the screen, save with their revision and 409, answer each
/// problem at its path, and what is saved is what the codes, the sends and the retention read next. The settings are one row for the whole
/// installation, so the tests use a database of their own.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppSettingsEndpointsTests(ApiFactory factory)
{
    private const string Route = "/api/whatsapp/settings";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Every_route_needs_a_session_and_the_settings_permission(string method)
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var whatsAppOnly = await host.OperatorAsync(WhatsAppPermissions.Read, WhatsAppPermissions.Send, WhatsAppPermissions.ConfigureAssistant);

        using var anonymous = await host.SendAsync(method, Route, null, method == "PUT" ? Settings(0) : null);
        using var forbidden = await host.SendAsync(method, Route, whatsAppOnly, method == "PUT" ? Settings(0) : null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task The_settings_read_the_values_of_before_save_with_their_revision_and_a_stale_save_is_a_conflict()
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var manager = await host.OperatorAsync(Permissions.Settings.Manage);

        using var defaults = await host.SendAsync("GET", Route, manager);
        Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);
        var read = await defaults.ReadJsonAsync();
        Assert.Equal(["allowedCountries", "flowSessionLifetimeHours", "messageRetentionDays", "revision"],
            read.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal((0, 24, 90), (read.GetProperty("revision").GetInt64(), read.GetProperty("flowSessionLifetimeHours").GetInt32(),
            read.GetProperty("messageRetentionDays").GetInt32()));
        Assert.Equal(["AR"], read.GetProperty("allowedCountries").EnumerateArray().Select(country => country.GetString()));

        using var saved = await host.SendAsync("PUT", Route, manager, Settings(0, 48, ["UY", "AR"], 30));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var settings = await saved.ReadJsonAsync();
        Assert.Equal((1, 48, 30), (settings.GetProperty("revision").GetInt64(), settings.GetProperty("flowSessionLifetimeHours").GetInt32(),
            settings.GetProperty("messageRetentionDays").GetInt32()));
        Assert.Equal(["AR", "UY"], settings.GetProperty("allowedCountries").EnumerateArray().Select(country => country.GetString()));

        using var stale = await host.SendAsync("PUT", Route, manager, Settings(0, 12));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("WhatsApp.Settings.RevisionConflict", (await stale.ReadJsonAsync()).GetProperty("code").GetString());

        using var again = await host.SendAsync("GET", Route, manager);
        var current = await again.ReadJsonAsync();
        Assert.Equal((1, 48), (current.GetProperty("revision").GetInt64(), current.GetProperty("flowSessionLifetimeHours").GetInt32()));
    }

    [Fact]
    public async Task An_invalid_save_answers_each_problem_at_its_path_and_an_unknown_property_is_rejected()
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var manager = await host.OperatorAsync(Permissions.Settings.Manage);

        using var invalid = await host.SendAsync("PUT", Route, manager, Settings(0, 169, ["AR", "ar"], 0));
        using var empty = await host.SendAsync("PUT", Route, manager, Settings(0, 24, [], 90));
        using var unknown = await host.SendAsync("PUT", Route, manager,
            new { expectedRevision = 0, flowSessionLifetimeHours = 24, allowedCountries = new[] { "AR" }, messageRetentionDays = 90, color = "red" });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("flowSessionLifetimeHours", out _));
        Assert.True(errors.TryGetProperty("allowedCountries[1]", out _));
        Assert.True(errors.TryGetProperty("messageRetentionDays", out _));
        Assert.True((await empty.ReadJsonAsync()).GetProperty("errors").TryGetProperty("allowedCountries", out _));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var unchanged = await host.SendAsync("GET", Route, manager);
        Assert.Equal(0, (await unchanged.ReadJsonAsync()).GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task What_is_saved_is_what_the_codes_the_sends_and_the_retention_read_next()
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var manager = await host.OperatorAsync(Permissions.Settings.Manage);
        var (older, younger) = await host.MessagesAsync(31, 29);

        using (var saved = await host.SendAsync("PUT", Route, manager, Settings(0, 6, ["AR", "UY"], 30)))
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var methods = await host.SendAsync("GET", "/account/login-methods", null);
        Assert.Equal(["AR", "UY"], (await methods.ReadJsonAsync()).GetProperty("whatsappCountries").EnumerateArray().Select(country => country.GetString()));
        await using (var scope = host.App.Services.CreateAsyncScope())
            Assert.Equal(TimeSpan.FromHours(6), await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowEnvironment>().FindSessionLifetimeAsync(Ct));
        await host.App.Services.GetRequiredService<WhatsAppMessageRetentionService>().ClearExpiredTextsAsync(Ct);
        Assert.Equal([null, "Hace 29 días"], await host.BodiesAsync(older, younger));
    }

    [Fact]
    public async Task The_presentation_says_the_module_is_installed()
    {
        await using var host = await Host.CreateAsync(factory, Ct);

        using var presentation = await host.SendAsync("GET", "/api/settings/presentation", null);

        Assert.Contains("whatsapp", (await presentation.ReadJsonAsync()).GetProperty("installedFeatures").EnumerateArray().Select(value => value.GetString()));
    }

    private static object Settings(long revision, int hours = 24, string[]? countries = null, int days = 90) =>
        new { expectedRevision = revision, flowSessionLifetimeHours = hours, allowedCountries = countries ?? ["AR"], messageRetentionDays = days };

    private sealed class Host : IAsyncDisposable
    {
        private Host(WebApplicationFactory<Program> app) => App = app;

        public WebApplicationFactory<Program> App { get; }

        public static async Task<Host> CreateAsync(ApiFactory factory, CancellationToken ct)
        {
            var app = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("whatsapp_settings")));
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync(ct);
            await app.Services.SeedDatabaseAsync(ct);
            return new Host(app);
        }

        public async Task<HttpResponseMessage> SendAsync(string method, string path, Guid? actor, object? body = null)
        {
            using var client = App.CreateClient();
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (actor is { } id) request.Headers.Add(TestAuthHandler.UserIdHeader, id.ToString());
            if (body is not null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request, Ct);
        }

        public async Task<Guid> OperatorAsync(params string[] permissions)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var roleName = "settings-" + Guid.NewGuid().ToString("N");
            Guid userId = Guid.Empty;
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

        // Two inbound texts of one contact, the given days ago.
        public async Task<(Guid Older, Guid Younger)> MessagesAsync(int olderDays, int youngerDays)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var nowUtc = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
            var contact = WhatsAppContact.Create(MetaWebhook.UniqueWaId(), MetaWebhook.UniqueBsuid(), "Persona", nowUtc);
            var older = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, $"Hace {olderDays} días", null,
                nowUtc.AddDays(-olderDays));
            var younger = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, $"Hace {youngerDays} días", null,
                nowUtc.AddDays(-youngerDays));
            db.AddRange(contact, older, younger);
            await db.SaveChangesAsync(Ct);
            return (older.Id, younger.Id);
        }

        public async Task<string?[]> BodiesAsync(params Guid[] ids)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
                .Where(message => ids.Contains(message.Id)).Select(message => new { message.Id, message.Body }).ToListAsync(Ct);
            return [.. ids.Select(id => rows.Single(row => row.Id == id).Body)];
        }

        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}
