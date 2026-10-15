using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FakeFormFileReader(Guid recordId, Guid fileId, bool isPhoto) : IFormFileReader
{
    public Task<FormFileAccessRow?> FindAsync(Guid record, Guid file, CancellationToken cancellationToken) =>
        Task.FromResult(record == recordId && file == fileId ? new FormFileAccessRow(fileId, isPhoto) : null);
}
