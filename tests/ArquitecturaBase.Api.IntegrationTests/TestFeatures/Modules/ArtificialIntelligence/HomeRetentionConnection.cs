using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

/// <summary>
/// RC1-A: una conexión que guarda las conversaciones de Inicio los días pedidos, para correr la retención con otro plazo sin
/// tocar la conexión que comparte la base del arnés.
/// </summary>
internal sealed class HomeRetentionConnection(int days) : IAiConnectionReader
{
    public Task<AiConnectionRow> FindAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AiConnectionRow(AiProvider.OpenAI, "fictional-model", null, false, 1, days));

    public Task<AiConnectionChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
