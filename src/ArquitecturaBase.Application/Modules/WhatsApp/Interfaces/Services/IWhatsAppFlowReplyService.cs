namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppFlowReplyService
{
    Task ProcessPendingAsync(CancellationToken cancellationToken);
}
