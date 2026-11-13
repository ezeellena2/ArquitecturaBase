namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// The pace of the outgoing to one person: after a send finishes, the next message to them waits six seconds. The lease in Redis
/// keeps it across hosts and the choice of the next chat skips whoever is still waiting (H12).
/// </summary>
public static class WhatsAppDispatchPacing
{
    public static readonly TimeSpan RecipientWait = TimeSpan.FromSeconds(6);
}
