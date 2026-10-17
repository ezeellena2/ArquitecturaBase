using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantOperations(IAiConnectionReader connectionReader, AiAssistantAccessGuard guard,
    AiAssistantSubmissionPolicy submissions, IUnitOfWork unitOfWork, IAiAssistantReader reader,
    AiAssistantResponsePolicy responsePolicy, ILogger<AiAssistantOperations> logger) : IAssistantOperations
{
    public async Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        var connection = await connectionReader.FindAsync(cancellationToken);
        return new AssistantAvailabilityResponse(guard.Availability(connection));
    }

    public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AssistantOperationAcceptedResponse>>(logger, "SubmitAssistantOperation", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (submissions.ValidateOperation(request) is { } error) return error;
            return await unitOfWork.ExecuteInTransactionAsync(ct => submissions.AcceptAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AssistantOperationResponse>>(logger, "GetAssistantOperation", async () =>
        {
            ArgumentNullException.ThrowIfNull(context);
            var row = await reader.FindTurnByRequestAsync(context.ActorUserId, requestId, cancellationToken);
            if (row is null || row.ChannelId == "home") return AiAssistantErrors.NotFound;
            var captured = AiAssistantChannelPolicy.Read(row.ExecutionProfileJson);
            if (captured.IsFailure || !AiAssistantChannelPolicy.Matches(captured.Value, row.OwnerUserId, row.ChannelId, row.Culture, row.TimeZoneId) ||
                !SameScope(context, captured.Value.ChannelContext)) return AiAssistantErrors.NotFound;
            var access = await guard.AuthorizeCapturedAsync(captured.Value.ChannelContext, cancellationToken);
            if (access.IsFailure) return access.Error;
            if (!await guard.CanReadAsync(context.ActorUserId, row.RequiredPermissions, cancellationToken)) return AiAssistantErrors.ContentBlocked;
            if (captured.Value.OccurredAtUtc is not { Kind: DateTimeKind.Utc } occurredAtUtc) return AiAssistantErrors.ContentUnavailable;
            var mapped = responsePolicy.Map(row, true, 0);
            if (mapped.IsFailure) return mapped.Error;
            var provenance = new AssistantOperationProvenance(captured.Value.ModelProfileId, captured.Value.ModelProfileVersion,
                captured.Value.InstructionsId, captured.Value.InstructionsVersion, occurredAtUtc, captured.Value.ChannelContext.ChannelId);
            return new AssistantOperationResponse(requestId, mapped.Value.State, mapped.Value.Proposal, provenance, row.FailureCode);
        });

    private static bool SameScope(AssistantChannelContext requested, AssistantChannelContext captured) =>
        requested.ChannelId == captured.ChannelId && requested.ActorUserId == captured.ActorUserId && requested.SubjectUserId == captured.SubjectUserId &&
        requested.Culture == captured.Culture && requested.TimeZoneId == captured.TimeZoneId &&
        captured.AllowedCapabilities.All(requested.AllowedCapabilities.Contains) &&
        requested.AllowedTerminals.ToHashSet().SetEquals(captured.AllowedTerminals) &&
        requested.RequiredPermissions.Actor.All(captured.RequiredPermissions.Actor.Contains) &&
        requested.RequiredPermissions.Subject.All(captured.RequiredPermissions.Subject.Contains);
}
