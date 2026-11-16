using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Hosting;

/// <summary>
/// N10: el código que ve la persona (el traceId) lleva al error. Fuera de Development y Testing la consola escribe JSON
/// con los scopes, entre ellos el traceId y la ruta que pone ASP.NET Core, y los logs de un pedido con sesión llevan el id
/// de quien pide; nunca su correo ni su número.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ObservabilityTests(ApiFactory factory)
{
    private const string UnhandledExceptionMessage = "Unhandled exception while processing the request";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    public void Outside_development_and_testing_the_console_writes_json_with_scopes(string environment, bool json)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.AddServiceDefaults();
        using var services = builder.Services.BuildServiceProvider();

        var console = services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>().CurrentValue;
        var formatter = services.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>().CurrentValue;

        Assert.Equal(json, console.FormatterName == ConsoleFormatterNames.Json);
        Assert.Equal(json, formatter.IncludeScopes);
    }

    [Fact]
    public async Task The_logs_of_a_request_with_a_session_carry_the_user_id()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddLogging(logging => logging.AddFakeLogging())));
        using var client = api.CreateClient();
        var userId = Guid.NewGuid();
        using var signedIn = new HttpRequestMessage(HttpMethod.Get, new Uri("/test/boom", UriKind.Relative));
        signedIn.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString("D", CultureInfo.InvariantCulture));

        using var withSession = await client.SendAsync(signedIn, Ct);
        using var anonymous = await client.GetAsync("/test/boom", Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, withSession.StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, anonymous.StatusCode);
        var failures = api.Services.GetFakeLogCollector().GetSnapshot()
            .Where(record => record.Level == LogLevel.Error && record.Message == UnhandledExceptionMessage)
            .ToList();
        Assert.Equal(2, failures.Count);
        Assert.Equal([userId], UserIds(failures[0]));
        Assert.Empty(UserIds(failures[1]));
    }

    private static List<object?> UserIds(FakeLogRecord record) =>
        [.. record.Scopes
            .OfType<IEnumerable<KeyValuePair<string, object?>>>()
            .SelectMany(scope => scope)
            .Where(pair => pair.Key == "UserId")
            .Select(pair => pair.Value)];
}
