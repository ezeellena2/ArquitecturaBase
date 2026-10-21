using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed class ControlledFormChannelStateSource : IStructuredFormChannelStateSource
{
    public IReadOnlyList<StructuredFormChannelStateRow> Rows { get; set; } = [];

    public Task<IReadOnlyList<StructuredFormChannelStateRow>> ListAsync(IReadOnlyCollection<Guid> definitionIds, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<StructuredFormChannelStateRow>>(Rows.Where(row => definitionIds.Contains(row.DefinitionId)).ToArray());
    }
}
