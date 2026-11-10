using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

// Stage 2: a Home turn with a spreadsheet, in a single request. The retry comes first and recovers its acceptance without reading,
// storing or accepting again; a new spreadsheet is read by its owner through the neutral port, keeps its private copy before the unit
// of work and goes with the turn through the same Home acceptance as any other.
internal sealed class AiAssistantFileTurnService(IUnitOfWork unitOfWork, IRequestValidator validator, AiAssistantAccessGuard guard,
    AiAssistantSubmissionPolicy submissions, AiAssistantHomeAttachmentPolicy attachments, AiAssistantLimits limits, TimeProvider clock,
    ILogger<AiAssistantFileTurnService> logger) : IAiAssistantFileTurnService
{
    public Task<Result<AiAssistantTurnAcceptedResponse>> SubmitAsync(SubmitAiAssistantFileTurnRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantTurnAcceptedResponse>>(logger, "SubmitAiAssistantFileTurn", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
            var actor = await guard.AuthorizeAsync(userId, cancellationToken);
            if (actor.IsFailure) return actor.Error;
            // The turn's fields before the file is read: all but the required question, which the spreadsheet stands in for.
            if (await validator.ValidateAsync(request.Turn with { Question = string.IsNullOrWhiteSpace(request.Turn.Question) ? "-" : request.Turn.Question },
                cancellationToken) is { } invalid) return invalid;
            var turn = await TurnAsync(userId, request, cancellationToken);
            if (turn.IsFailure) return turn.Error;
            return await unitOfWork.ExecuteInTransactionAsync(ct => submissions.AcceptAsync(userId, turn.Value, ct), CommitPolicy.OnSuccess, cancellationToken);
        });

    // The turn with its spreadsheet, validated before anything is kept: a retry's own, or a new one read and stored.
    private async Task<Result<SubmitAiAssistantTurnRequest>> TurnAsync(Guid userId, SubmitAiAssistantFileTurnRequest request, CancellationToken ct)
    {
        var name = AiAssistantHomeAttachmentPolicy.Name(request.FileName);
        var retried = await attachments.FindRetryAsync(userId, request.Turn.RequestId, request.Content, name, ct);
        if (retried.IsFailure) return retried.Error;
        if (retried.Value is { } accepted) return await ValidateAsync(request.Turn with { Attachment = accepted }, ct);
        if (await attachments.CheckAttachAsync(userId, ct) is { } refused) return refused;
        var prepared = await attachments.PrepareAsync(request.Content, name, ct);
        if (prepared.IsFailure) return prepared.Error;
        var attachment = AiAssistantHomeAttachmentPolicy.Describe(request.Turn.RequestId, request.Content, name, prepared.Value,
            clock.GetUtcNow().UtcDateTime.AddDays(limits.RetentionDays));
        if (AiAssistantAttachmentPolicy.Validate(attachment) is not null) return FieldErrors.On(AiAssistantErrors.Invalid, "file");
        var turn = await ValidateAsync(request.Turn with { Attachment = attachment }, ct);
        if (turn.IsFailure) return turn;
        var stored = await attachments.StoreAsync(attachment, request.Content, ct);
        return stored.IsFailure ? stored.Error : turn;
    }

    private async Task<Result<SubmitAiAssistantTurnRequest>> ValidateAsync(SubmitAiAssistantTurnRequest turn, CancellationToken ct)
    {
        if (await validator.ValidateAsync(turn, ct) is { } invalid) return invalid;
        return turn;
    }
}
