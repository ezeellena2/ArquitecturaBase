using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Settings;

/// <summary>
/// Registration:Mode es el valor que el seed escribe en la base al crear la fila de ajustes, también en Production. El
/// binder acepta cualquier número como enum: la validación de opciones tiene que rechazar el que no es un modo, así
/// ValidateOnStart y la validación previa de DatabaseInitialization cortan antes de tocar la base. Sin Docker: solo
/// se arman los servicios, la base no se abre.
/// </summary>
public sealed class RegistrationOptionsTests
{
    [Theory]
    [InlineData("InviteOnly")]
    [InlineData("Open")]
    [InlineData("0")]
    [InlineData("1")]
    public void A_defined_mode_passes_the_startup_validation(string mode)
    {
        using var provider = BuildProvider(mode);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData("5")]
    [InlineData("-1")]
    public void A_number_that_is_not_a_mode_is_rejected_at_startup_validation(string mode)
    {
        using var provider = BuildProvider(mode);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(exception.Failures, failure => failure.Contains("Registration:Mode", StringComparison.Ordinal));
    }

    private static ServiceProvider BuildProvider(string mode)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:" + Infrastructure.DependencyInjection.DatabaseConnectionName] = "Host=localhost;Database=unused",
                ["Registration:Mode"] = mode,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPersistence(configuration);

        return services.BuildServiceProvider();
    }
}
