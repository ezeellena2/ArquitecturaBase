using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// The business information a channel's assistant may use as knowledge (Casos del asistente): only confirmed facts, with the
/// state of each zone. It only reads. The core answers «not installed» and the module that owns the information replaces it.
/// </summary>
public interface IBusinessKnowledgeSource
{
    Task<BusinessKnowledge> ReadAsync(string culture, CancellationToken cancellationToken);
}
