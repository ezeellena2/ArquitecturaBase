using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed class ReadyStructuredFormChannel(TimeProvider timeProvider) : IStructuredFormChannel
{
    public Task<Result<StructuredFormPublicationResponse>> AcceptAsync(StructuredFormPublicationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public StructuredFormPublicationResponse? PublicationResponse { get; set; }
    public Task<Result<StructuredFormPublicationResponse>> GetPublicationAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<StructuredFormPublicationResponse>>(PublicationResponse ?? throw new NotSupportedException());
    public TimeSpan PreviewLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<StructuredFormPreviewResponse>>(new StructuredFormPreviewResponse("https://business.facebook.com/wa/manage/flows/test/preview/", timeProvider.GetUtcNow().UtcDateTime.Add(PreviewLifetime)));

    public string ChannelId => "test";
    public Task<Result<StructuredFormCheckResponse>> CheckAsync(StructuredFormCheckRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<StructuredFormCheckResponse>>(new StructuredFormCheckResponse("test-profile", "LocallyValidated", "Pending",
            request.TargetCultures.Select(culture => new StructuredFormCultureResponse(culture, "test-" + culture, 1)).ToArray(), [], true));
}
