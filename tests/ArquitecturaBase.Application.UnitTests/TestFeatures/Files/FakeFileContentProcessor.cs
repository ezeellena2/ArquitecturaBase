using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
internal sealed class FakeFileContentProcessor : IFileContentProcessor
{
    public Result<ProcessedPrivateFile> Process(byte[] content, string contentType, string expectedSha256, long maximumBytes, bool imageOnly) =>
        expectedSha256 == "bad" ? FileErrors.HashMismatch : new ProcessedPrivateFile(expectedSha256, imageOnly ? [4, 5] : null);
}
