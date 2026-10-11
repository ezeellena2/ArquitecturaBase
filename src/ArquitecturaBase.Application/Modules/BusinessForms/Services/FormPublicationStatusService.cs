using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormPublicationStatusService(IFormPublicationReader reader, FormPublicationAccessGuard accessGuard,
    IFormPublicationProtector protector, IEnumerable<IStructuredFormChannel> channels, ILogger<FormPublicationStatusService> logger) : IFormPublicationStatusService
{
    public Task<Result<FormPublicationStatusResponse>> GetAsync(Guid id, Guid requestId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPublicationStatusResponse>>(logger, "GetFormPublicationStatus", async () =>
    {
        var access = await accessGuard.CheckAsync(cancellationToken); if (access.IsFailure) return access.Error;
        var result = await reader.FindOperationAsync(id, access.Value, requestId, cancellationToken);
        if (result is null) return FormPublicationErrors.NotFound;
        var issues = new List<FormPublicationIssueResponse>();
        if (result.State is "Failed" or "Uncertain")
        {
            var stored = await reader.FindStoredAsync(result.OperationId, cancellationToken) ?? throw new InvalidOperationException("The publication manifest is missing.");
            var manifest = protector.ReadManifest(new(stored.Id, stored.Actor, stored.RequestId, stored.DefinitionId, stored.VersionId), stored.ProtectedManifest);
            foreach (var profile in manifest.Profiles)
            {
                var channel = channels.SingleOrDefault(value => value.ChannelId == profile.ChannelId);
                if (channel is null) { issues.Add(Issue(profile.ChannelId, FormPublicationErrors.ChannelUnavailable)); continue; }
                var status = await channel.GetPublicationAsync(new(stored.Id, stored.DefinitionId, stored.VersionId, manifest.PublicationFingerprint), cancellationToken);
                if (status.IsFailure) { issues.Add(Issue(profile.ChannelId, FormPublicationErrors.AccountUnavailable)); continue; }
                issues.AddRange(status.Value.Issues.Select(issue => new FormPublicationIssueResponse(profile.ChannelId, issue.FieldId, issue.ChoiceId,
                    issue.Path, issue.Error.Code, ErrorMessages.Get(issue.Error.Code), issue.Limit, issue.Culture)));
            }
        }
        return result with { Issues = issues.ToArray() };
    });
    private static FormPublicationIssueResponse Issue(string channel, Error error) => new(channel, null, null, "channel", error.Code, ErrorMessages.Get(error.Code), null, null);
}
