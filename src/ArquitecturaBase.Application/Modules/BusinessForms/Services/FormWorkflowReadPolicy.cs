using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormWorkflowReadPolicy
{
    private readonly IFormActionRunReader _reader; private readonly FormRecordAccessGuard _access; private readonly IPermissionService _permissions;
    private readonly IStructuredRecordActionChannel[] _channels; private readonly IStructuredRecordIssuer[] _issuers;
    public FormWorkflowReadPolicy(IFormActionRunReader reader, FormRecordAccessGuard access, IPermissionService permissions, IEnumerable<IStructuredRecordActionChannel> channels,
        IEnumerable<IStructuredRecordIssuer> issuers)
    {
        _reader = reader; _access = access; _permissions = permissions; _channels = channels.ToArray(); _issuers = issuers.ToArray();
        if (_channels.Any(channel => string.IsNullOrWhiteSpace(channel.ChannelId)) || _channels.DistinctBy(channel => channel.ChannelId).Count() != _channels.Length)
            throw new InvalidOperationException("Action channel identifiers must be unique and nonempty.");
    }
    public async Task<Result<FormWorkflowResponse>> ReadAsync(Guid recordId, CancellationToken cancellationToken)
    {
        var read = await _access.CheckReadAsync(cancellationToken); if (read.IsFailure) return read.Error;
        if (!await _access.CanReadAsync(read.Value, recordId, cancellationToken)) return FormRecordErrors.NotFound;
        var result = await _reader.FindWorkflowAsync(recordId, cancellationToken); if (result is null) return FormRecordErrors.NotFound;
        result = result with { Confirmation = await _reader.FindIssueConfirmationAsync(recordId, read.Value.Actor, cancellationToken) };
        // Whoever only loads sees where their response stands (state, decision and PDF), without the internal actions (CW6-C).
        if (read.Value.OwnOnly)
            return await _access.StillAllowedAsync(read.Value, cancellationToken) ? result with { Actions = [] } : FormDefinitionErrors.Forbidden;
        var observations = new Dictionary<(string ChannelId, Guid RunId, Guid UserId), StructuredRecordActionResponse>();
        foreach (var channel in _channels)
        {
            var requests = result.Actions.SelectMany(action => action.Recipients.Where(recipient => recipient.ChannelId == channel.ChannelId)
                .Select(recipient => new StructuredRecordActionObservationRequest(action.Id, recipient.User.UserId))).Distinct().ToArray();
            if (requests.Length == 0) continue; var requested = requests.ToHashSet();
            foreach (var observation in await channel.ObserveAsync(requests, cancellationToken))
                if (requested.Contains(new(observation.RunId, observation.RecipientUserId)))
                    observations.Add((channel.ChannelId, observation.RunId, observation.RecipientUserId), observation.Status);
        }
        var manage = await _permissions.HasPermissionAsync(read.Value.Actor, BusinessFormsPermissions.RulesManage, cancellationToken);
        var reviewIssues = manage && result.Actions.Any(action => action.Kind == "IssueDocument" && action.State == "Uncertain")
            && await CanReviewIssuesAsync(read.Value.Actor, cancellationToken);
        if (!await _access.StillAllowedAsync(read.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
        return result with
        {
            Actions = result.Actions.Select(action =>
            {
                var recipients = action.Recipients.Select(recipient => Apply(action, recipient, manage, recipient.ChannelId is { } channelId
                    ? observations.GetValueOrDefault((channelId, action.Id, recipient.User.UserId)) : null)).ToArray();
                return action with
                {
                    // What the provider said of a failed issuance comes as the reader found it; any other reason, by its code.
                    Reason = action.Reason ?? (action.ReasonCode is { } code ? ErrorMessages.Get(code) : null),
                    CanRetry = manage && action.CanRetry && !(recipients.Length > 0 && recipients.All(recipient => recipient.State == "Accepted")),
                    CanConsult = manage && action.CanConsult,
                    CanReview = reviewIssues && action.Kind == "IssueDocument" && action.State == "Uncertain",
                    Recipients = recipients
                };
            }).ToArray()
        };
    }
    /// <summary>The permission to decide on an issuance in review of an installed issuer; null if it is not installed.</summary>
    public string? ReviewPermission(string? issuerId) => _issuers.FirstOrDefault(issuer => issuer.IssuerId == issuerId)?.ReviewPermission;
    private async Task<bool> CanReviewIssuesAsync(Guid actor, CancellationToken cancellationToken)
    {
        foreach (var issuer in _issuers)
            if (!await _permissions.HasPermissionAsync(actor, issuer.ReviewPermission, cancellationToken)) return false;
        return _issuers.Length > 0;
    }
    private static FormActionRecipientResponse Apply(FormActionRunResponse action, FormActionRecipientResponse recipient, bool manage, StructuredRecordActionResponse? observation)
    {
        var useObserved = observation is not null && observation.State != StructuredRecordActionState.NotFound
            && (recipient.State != "Accepted" || observation.State == StructuredRecordActionState.Accepted);
        var state = useObserved ? observation!.State.ToString() : recipient.State; var reasonCode = useObserved ? observation!.FailureCode : recipient.ReasonCode;
        return recipient with
        {
            State = state,
            ReasonCode = reasonCode,
            Reason = reasonCode is { } code ? ErrorMessages.Get(code) : null,
            CanReview = manage && action.State == "Uncertain" && (action.Kind is "NotifyAuthor" or "NotifyRole") && state == "Uncertain",
            // The message to whoever loaded it is retried as a whole action; a person of a finished role notice, alone.
            CanRetry = manage && action.Kind == "NotifyRole" && action.State == "Completed" && state == "Failed" && recipient.CanRetry,
            DeliveredAtUtc = observation?.Receipt?.DeliveredAtUtc ?? recipient.DeliveredAtUtc,
            ReadAtUtc = observation?.Receipt?.ReadAtUtc ?? recipient.ReadAtUtc
        };
    }
}
