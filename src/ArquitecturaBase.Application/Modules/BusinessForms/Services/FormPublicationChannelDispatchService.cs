using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPublicationChannelDispatchService(IEnumerable<IStructuredFormChannel> channels,
    ILogger<FormPublicationChannelDispatchService> logger) : IFormPublicationChannelDispatchService
{
    public Task<Result<FormPublicationChannelDispatchResponse>> DispatchAsync(FormPublicationWorkResponse work,
        CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPublicationChannelDispatchResponse>>(logger, "DispatchFormPublicationChannels", async () =>
        {
            var progress = new List<FormPublicationChannelProgress>();
            foreach (var channelRequest in work.ChannelRequests)
            {
                var channel = channels.SingleOrDefault(value => value.ChannelId == channelRequest.ChannelId);
                if (channel is null)
                {
                    progress.Add(new(channelRequest.ChannelId, Uncertain(work, "ChannelUnavailable")));
                    continue;
                }
                if (!work.ReconciliationOnly)
                {
                    var accepted = await channel.AcceptAsync(channelRequest.Request, cancellationToken);
                    if (accepted.IsFailure)
                    {
                        progress.Add(new(channel.ChannelId, Uncertain(work, "ChannelUnavailable")));
                        continue;
                    }
                    if (accepted.Value.State == "Failed")
                    {
                        progress.Add(new(channel.ChannelId, accepted.Value));
                        continue;
                    }
                }
                var observed = await channel.GetPublicationAsync(new(work.OperationId, work.DefinitionId, work.VersionId,
                    work.Manifest.PublicationFingerprint), cancellationToken);
                var response = observed.IsSuccess ? observed.Value
                    : work.ReconciliationOnly && observed.Error.Type == ErrorType.NotFound
                        ? FailedWithoutEffect(work)
                        : Uncertain(work, "ChannelUnavailable");
                progress.Add(new(channel.ChannelId, response));
            }
            return new FormPublicationChannelDispatchResponse(progress);
        });

    private static StructuredFormPublicationResponse FailedWithoutEffect(FormPublicationWorkResponse work) =>
        new(work.OperationId, work.DefinitionId, work.VersionId, work.Manifest.PublicationFingerprint,
            "Failed", "AuthorizationChanged", [], null, false);

    private static StructuredFormPublicationResponse Uncertain(FormPublicationWorkResponse work, string stage) =>
        new(work.OperationId, work.DefinitionId, work.VersionId, work.Manifest.PublicationFingerprint, "Uncertain", stage, [], null, true);
}
