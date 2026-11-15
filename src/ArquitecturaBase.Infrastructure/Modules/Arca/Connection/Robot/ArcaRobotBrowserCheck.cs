using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;

/// <summary>
/// Con el robot prendido, la Api no arranca sin Chromium (instalado y que abra, H22) ni con la depuración de Playwright encendida (un <c>DEBUG</c> con
/// <c>*</c> o <c>pw</c>, H21, o <c>PWDEBUG</c>), que escribe en los logs cada acción, también lo que se escribe en la página
/// (ADR 0026). Es una falla del despliegue y conviene verla al publicar, no en la primera corrida de un cliente.
/// </summary>
internal sealed class ArcaRobotBrowserCheck(IOptions<ArcaRobotBrowserOptions> options) : IHostedService
{
    /// <summary>H21: un <c>DEBUG</c> con <c>*</c> (como <c>DEBUG=*</c>, común para depurar Node) o <c>pw</c>, o <c>PWDEBUG</c>.</summary>
    internal static bool PlaywrightDebugging(Func<string, string?> variable)
    {
        var debug = variable("DEBUG") ?? string.Empty;
        return debug.Contains('*', StringComparison.Ordinal) || debug.Contains("pw", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(variable("PWDEBUG"));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return;
        if (PlaywrightDebugging(Environment.GetEnvironmentVariable))
            throw new InvalidOperationException("The ARCA robot does not run with Playwright debugging on: it would log what is typed.");

        using var playwright = await Playwright.CreateAsync();
        if (!File.Exists(playwright.Chromium.ExecutablePath))
            throw new InvalidOperationException("The ARCA robot is on but Chromium is not installed; see docs/guides/robot-arca.md.");

        // H22: abre y cierra Chromium: una imagen a la que le falta una biblioteca del sistema no arranca el navegador.
        try
        {
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            await browser.CloseAsync();
        }
        catch (PlaywrightException exception)
        {
            throw new InvalidOperationException(
                $"The ARCA robot is on but Chromium does not start ({exception.GetType().Name}); see docs/guides/robot-arca.md.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
