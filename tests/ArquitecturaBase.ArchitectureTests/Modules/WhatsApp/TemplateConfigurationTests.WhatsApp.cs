namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// La parte del módulo WhatsApp de TemplateConfigurationTests (B1): la plantilla trae WhatsApp apagado, sin el número ni
/// la cuenta de Meta de una instalación. Lleva el namespace de la clase del núcleo, no el de su carpeta, para usar su
/// lectura de la configuración.
/// </summary>
public sealed partial class TemplateConfigurationTests
{
    /// <summary>El interruptor (<c>PhoneNumberId</c>) y lo demás que es de un número de Meta en particular.</summary>
    private static readonly string[] WhatsAppInstallationKeys =
    [
        "WhatsApp:PhoneNumberId",
        "WhatsApp:BusinessAccountId",
        "WhatsApp:WabaId",
        "WhatsApp:DisplayPhoneNumber",
        "WhatsApp:SendArgentineMobilesWithoutNine",
    ];

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void The_versioned_configuration_comes_with_whatsapp_off(string environment) =>
        AssertUnset(environment, WhatsAppInstallationKeys);
}
