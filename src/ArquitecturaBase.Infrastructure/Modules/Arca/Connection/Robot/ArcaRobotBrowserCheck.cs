using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Robot;

/// <summary>
/// Con el robot prendido, la Api no arranca sin Chromium ni con la depuración de Playwright encendida (<c>DEBUG=pw:*</c> o
/// <c>PWDEBUG</c>), que escribe en los logs cada acción, también lo que se escribe en la página (ADR 0026). Es una falla del
/// despliegue y conviene verla al publicar, no en la primera corrida de un cliente.
/// </summary>
internal sealed class ArcaRobotBrowserCheck(IOptions<ArcaRobotBrowserOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) return;
        if ((Environment.GetEnvironmentVariable("DEBUG") ?? string.Empty).Contains("pw", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PWDEBUG")))
            throw new InvalidOperationException("The ARCA robot does not run with Playwright debugging on: it would log what is typed.");

        using var playwright = await Playwright.CreateAsync();
        if (!File.Exists(playwright.Chromium.ExecutablePath))
            throw new InvalidOperationException("The ARCA robot is on but Chromium is not installed; see docs/guides/robot-arca.md.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
