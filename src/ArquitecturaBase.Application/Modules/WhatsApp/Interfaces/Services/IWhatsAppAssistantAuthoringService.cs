using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>Answers a tap on an authoring summary's buttons (IA3-A): create, change something, or say that it changed.</summary>
public interface IWhatsAppAssistantAuthoringService
{
    Task<Result> ProcessTapAsync(Guid contactId, CancellationToken ct);
}
