using Microsoft.Extensions.Configuration;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// B1: la configuración versionada de la Api es la de la plantilla, no la de una instalación. Viene con Google apagado,
/// sin el correo de una persona ni el cliente de Google, y en Development escribe los correos como .eml: un clon nuevo
/// arranca sin más secretos que los anillos de claves que exigen los módulos que cifran datos. Cada instalación carga lo
/// suyo en user-secrets o en variables de entorno (README, "Configuración de desarrollo"). Cada módulo suma sus claves en
/// su parte de esta clase.
/// </summary>
public sealed partial class TemplateConfigurationTests
{
    /// <summary>Los interruptores y los datos de una instalación: ausentes o vacíos en lo versionado.</summary>
    private static readonly string[] InstallationKeys =
    [
        "Authentication:Google:ClientId",
        "Email:Smtp:UserName",
        "Email:Smtp:FromAddress",
        "Seed:AdminEmail",
    ];

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void The_versioned_configuration_carries_no_installation_data(string environment) =>
        AssertUnset(environment, InstallationKeys);

    [Fact]
    public void Development_writes_the_emails_as_files_so_it_starts_without_the_smtp_password() =>
        Assert.Equal("PickupDirectory", Read("Development")["Email:Delivery"]);

    private static void AssertUnset(string environment, IEnumerable<string> keys)
    {
        var configuration = Read(environment);

        Assert.DoesNotContain(keys, key => !string.IsNullOrEmpty(configuration[key]));
    }

    /// <summary>Como el host: appsettings.json y encima el del ambiente, sin user-secrets ni variables de entorno.</summary>
    private static IConfiguration Read(string environment) => new ConfigurationBuilder()
        .SetBasePath(Path.Combine(SolutionRoot.FullPath, "src", "ArquitecturaBase.Api"))
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{environment}.json", optional: true)
        .Build();
}
