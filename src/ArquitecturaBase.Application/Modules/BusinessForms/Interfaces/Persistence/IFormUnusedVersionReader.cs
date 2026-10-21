using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormUnusedVersionReader
{
    Task<IReadOnlyList<StructuredFormVersionReference>> ListUnusedAsync(IReadOnlyCollection<StructuredFormVersionReference> candidates,
        CancellationToken cancellationToken);
}
