using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Caching;

/// <summary>
/// El prefijo de Redis separa instalaciones y ambientes (N19). Fuera de Development y Testing es obligatorio: el que se
/// deriva de aplicación, ambiente y base no distingue dos instalaciones que comparten Redis con bases del mismo nombre, y
/// una leería el modo de registro de la otra. Corre el mismo validador que el host antes de arrancar
/// (<see cref="IStartupValidator"/>), como EmailRegistrationTests, sin Redis.
/// </summary>
public sealed class CachingRegistrationTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Outside_development_and_testing_the_api_does_not_start_without_a_key_prefix(string environment)
    {
        using var provider = BuildProvider(environment, keyPrefix: null);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(CachingRegistration.MissingKeyPrefixMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_a_key_prefix_the_api_starts_in_production()
    {
        using var provider = BuildProvider("Production", keyPrefix: "acme:production");

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal("acme:production", provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value.KeyPrefix);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void In_development_and_testing_the_prefix_comes_from_the_application_environment_and_database(string environment)
    {
        using var provider = BuildProvider(environment, keyPrefix: null);

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal($"Tests:{environment}:appdb", provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value.KeyPrefix);
    }

    private static ServiceProvider BuildProvider(string environment, string? keyPrefix)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:appdb"] = "Host=localhost;Database=appdb",
                ["Caching:KeyPrefix"] = keyPrefix,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCaching(configuration, new TestHostEnvironment { EnvironmentName = environment });

        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
