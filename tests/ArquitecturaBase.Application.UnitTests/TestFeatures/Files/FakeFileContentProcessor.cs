using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
internal sealed class FakeFileContentProcessor : IFileContentProcessor
{
    /// <summary>The limit of the last file it checked.</summary>
    public long? LastMaximumBytes { get; private set; }
    public Result<ProcessedPrivateFile> Process(byte[] content, string contentType, string expectedSha256, long maximumBytes, bool imageOnly)
    {
        LastMaximumBytes = maximumBytes;
        return expectedSha256 == "bad" ? FileErrors.HashMismatch : new ProcessedPrivateFile(expectedSha256, imageOnly ? [4, 5] : null);
    }
}
