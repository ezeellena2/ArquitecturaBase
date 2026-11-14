using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// H16: fixes the protector of the chat content when the copy starts, before any worker starts (<c>StartingAsync</c> runs before
/// every <c>StartAsync</c>), whatever the order they were registered in.
/// </summary>
internal sealed class WhatsAppChatContentStartup(IDataProtectionProvider provider) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        WhatsAppChatContent.Use(provider);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
