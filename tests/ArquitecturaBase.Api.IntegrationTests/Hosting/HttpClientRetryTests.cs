using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Hosting;

/// <summary>
/// N18: ningún cliente HTTP de la Api repite un POST ante un 503. Un POST repetido a un proveedor duplica el efecto (dos
/// cobros, dos mensajes), y lo que conviene reintentar lo reintenta la cola de cada módulo. Recorre los clientes con nombre
/// que registran el núcleo y los módulos, el de por defecto y uno que nadie configuró, como el que se agregue mañana:
/// ServiceDefaults no les pone resiliencia, y cada cliente declara la suya.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class HttpClientRetryTests(ApiFactory factory)
{
    private const string UnconfiguredClient = "a-client-nobody-configured";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task No_http_client_repeats_a_post_that_got_a_503()
    {
        var calls = new Dictionary<string, int>();
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Con el reloj real: con el de los tests, la espera de un reintento no terminaría nunca.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(TimeProvider.System);

            // Después de la configuración de cada cliente: todos terminan en un proveedor que responde 503.
            services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(
                handler => handler.PrimaryHandler = new UnavailableHandler(handler.Name ?? Options.DefaultName, calls)));
        }));
        var names = api.Services.GetServices<IConfigureOptions<HttpClientFactoryOptions>>()
            .Select(configure => configure.GetType().GetProperty("Name")?.GetValue(configure) as string)
            .OfType<string>()
            .Append(Options.DefaultName)
            .Append(UnconfiguredClient)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var handlers = api.Services.GetRequiredService<IHttpMessageHandlerFactory>();

        foreach (var name in names)
        {
            using var invoker = new HttpMessageInvoker(handlers.CreateHandler(name), disposeHandler: false);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://provider.invalid/charge"))
            {
                Content = new StringContent("{}"),
            };

            using var response = await invoker.SendAsync(request, Ct);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(calls[name] == 1, $"The HTTP client '{name}' sent the POST {calls[name]} times.");
        }

        // Los clientes de los módulos están en la cuenta, no sólo los dos de control.
        Assert.True(names.Length > 2, string.Join(", ", names));
    }

    private sealed class UnavailableHandler(string name, Dictionary<string, int> calls) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (calls)
            {
                calls[name] = calls.GetValueOrDefault(name) + 1;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
