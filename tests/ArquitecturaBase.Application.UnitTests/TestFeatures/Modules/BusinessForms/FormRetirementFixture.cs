using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormRetirementFixture : IFormUnusedVersionReader
{
    public FormLifecycleFixture Drafts { get; } = new();
    public int Reads { get; private set; }
    public IReadOnlyCollection<StructuredFormVersionReference> Candidates { get; private set; } = [];
    public IReadOnlyList<StructuredFormVersionReference> Unused { get; set; } = [];
    public Exception? Failure { get; set; }
    public CancellationToken ReadCancellation { get; private set; }
    public IStructuredFormRetirementSource Source { get; }
    public FormRetirementFixture() => Source = new BusinessFormRetirementSource(Drafts, this);
    public Task<IReadOnlyList<StructuredFormVersionReference>> ListUnusedAsync(IReadOnlyCollection<StructuredFormVersionReference> candidates,
        CancellationToken cancellationToken)
    {
        Reads++; Candidates = candidates.ToArray(); ReadCancellation = cancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is {} failure) throw failure;
        return Task.FromResult(Unused);
    }
}
