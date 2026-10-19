using System.Runtime.CompilerServices;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AudioNormalizationFiles(byte[]? bytes) : IPrivateFileStore
{
    public int Reads { get; private set; }
    public Action? BeforeRead { get; set; }
    public bool FailDuringStreamRead { get; set; }
    public Exception? StreamReadFailure { get; set; }
    public bool IsAvailable => true;
    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        Reads++; BeforeRead?.Invoke();
        return Task.FromResult<Stream?>(bytes is null ? null : FailDuringStreamRead || StreamReadFailure is not null
            ? new FailingAudioReadStream(bytes, StreamReadFailure) : new MemoryStream(bytes, false));
    }
    public Task<bool> WriteOnceAsync(string key, Stream content, string contentType, CancellationToken ct) => throw new NotSupportedException();
    public Task WriteAsync(string key, Stream content, string contentType, CancellationToken ct) => throw new NotSupportedException();
    public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    public async IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc, [EnumeratorCancellation] CancellationToken ct)
    { await Task.CompletedTask; yield break; }
}
internal sealed class AudioNormalizationClient : ILlmAudioTranscriptionClient
{
    public int Calls { get; private set; }
    public LlmAudioTranscriptionRequest? Last { get; private set; }
    public Task<Result<LlmAudioTranscriptionResponse>> TranscribeAsync(LlmAudioTranscriptionRequest request, CancellationToken ct)
    {
        Calls++; Last = request;
        return Task.FromResult<Result<LlmAudioTranscriptionResponse>>(new LlmAudioTranscriptionResponse("Visit at North branch", ["en"], null, 2.75m));
    }
}

internal sealed class FailingAudioReadStream(byte[] bytes, Exception? failure = null) : MemoryStream(bytes, false)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        ValueTask.FromException<int>(failure ?? new Azure.RequestFailedException(503, "Synthetic private storage read failure."));
}
