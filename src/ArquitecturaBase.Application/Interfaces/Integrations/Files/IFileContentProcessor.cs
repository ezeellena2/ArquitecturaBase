using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Interfaces.Integrations.Files;

public interface IFileContentProcessor
{
    Result<ProcessedPrivateFile> Process(byte[] content, string contentType, string expectedSha256, long maximumBytes, bool imageOnly);
}
