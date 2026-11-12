using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Channels;

/// <summary>Without the module that owns the business information there is no knowledge: the assistant uses only its cases.</summary>
internal sealed class UnavailableBusinessKnowledgeSource : IBusinessKnowledgeSource
{
    public Task<BusinessKnowledge> ReadAsync(string culture, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BusinessKnowledge.NotInstalled);
    }
}
