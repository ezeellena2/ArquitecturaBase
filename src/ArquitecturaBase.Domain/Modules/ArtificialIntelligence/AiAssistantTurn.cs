using System.Text;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public enum AiAssistantTurnState { Pending, Running, Completed, Failed, Uncertain, Canceled, Expired }

public sealed class AiAssistantTurn : Entity, IAuditable
{
    public const int MaximumExecutionProfileBytes = 16384;
    private AiAssistantTurn() { }
    public Guid OwnerUserId { get; private set; }
    public string ChannelId { get; private set; } = "home";
    public Guid? ChannelConversationId { get; private set; }
    public Guid ConversationId { get; private set; }
    public AiConversation Conversation { get; private set; } = null!;
    public Guid RequestId { get; private set; }
    public int Ordinal { get; private set; }
    public AiAssistantTurnState State { get; private set; }
    public string Stage { get; private set; } = "pending";
    public long Revision { get; private set; } = 1;
    public string? ProtectedQuestion { get; private set; }
    public string? ProtectedAnswer { get; private set; }
    public string? ProtectedEvidences { get; private set; }
    public long PayloadRevision { get; private set; } = 1;
    public string Culture { get; private set; } = "es";
    public string TimeZoneId { get; private set; } = "UTC";
    public AiProvider Provider { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public long ConnectionRevision { get; private set; }
    public string? ExecutionProfileJson { get; private set; }
    public Guid? AttemptId { get; private set; }
    public long Fence { get; private set; }
    public DateTime SubmittedAtUtc { get; private set; }
    public DateTime ContentExpiresAtUtc { get; private set; }
    public DateTime? DeadlineAtUtc { get; private set; }
    public DateTime? CancelRequestedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public int ProviderSteps { get; private set; }
    public int ToolCalls { get; private set; }
    public int ToolBytes { get; private set; }
    public long? InputTokens { get; private set; }
    public long? OutputTokens { get; private set; }
    public decimal? AudioSeconds { get; private set; }
    public bool UsageUnknown { get; private set; }
    public int ReportedUsageSteps { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static AiAssistantTurn Create(Guid ownerUserId, Guid conversationId, Guid requestId, int ordinal,
        string protectedQuestion, string culture, string timeZoneId, AiProvider provider, string model,
        long connectionRevision, DateTime submittedAtUtc, DateTime expiresAtUtc, string channelId = "home", Guid? channelConversationId = null) => new()
    {
        OwnerUserId = ownerUserId, ConversationId = conversationId, ChannelId = channelId, ChannelConversationId = channelConversationId, RequestId = requestId, Ordinal = ordinal,
        ProtectedQuestion = protectedQuestion, Culture = culture, TimeZoneId = timeZoneId,
        Provider = provider, Model = model, ConnectionRevision = connectionRevision,
        SubmittedAtUtc = submittedAtUtc, ContentExpiresAtUtc = expiresAtUtc,
    };

    public Result SealExecutionProfile(string executionProfileJson)
    {
        if (ExecutionProfileJson is not null)
            return string.Equals(ExecutionProfileJson, executionProfileJson, StringComparison.Ordinal) ? Result.Success() : AiAssistantErrors.RevisionConflict;
        if (State != AiAssistantTurnState.Pending) return AiAssistantErrors.RevisionConflict;
        if (string.IsNullOrWhiteSpace(executionProfileJson)) return AiAssistantErrors.Invalid;
        if (Encoding.UTF8.GetByteCount(executionProfileJson) > MaximumExecutionProfileBytes) return AiAssistantErrors.LimitExceeded;
        ExecutionProfileJson = executionProfileJson;
        AdvanceRevision();
        return Result.Success();
    }

    public void ProtectQuestion(string protectedQuestion) => ProtectedQuestion = protectedQuestion;

    public Result Claim(Guid attemptId, DateTime deadlineAtUtc)
    {
        if (State != AiAssistantTurnState.Pending || CancelRequestedAtUtc is not null) return AiAssistantErrors.FenceRejected;
        State = AiAssistantTurnState.Running;
        Stage = "preparing";
        AttemptId = attemptId;
        Fence = checked(Fence + 1);
        DeadlineAtUtc = deadlineAtUtc;
        AdvanceRevision();
        return Result.Success();
    }

    public Result VerifyFence(long fence, DateTime nowUtc)
    {
        if (State != AiAssistantTurnState.Running || Fence != fence) return AiAssistantErrors.FenceRejected;
        if (CancelRequestedAtUtc is not null) return AiAssistantErrors.Canceled;
        if (ContentExpiresAtUtc <= nowUtc || DeadlineAtUtc <= nowUtc) return AiAssistantErrors.Expired;
        return Result.Success();
    }

    public Result BeginProviderStep(long fence, DateTime nowUtc, int maximumSteps)
    {
        var valid = VerifyFence(fence, nowUtc);
        if (valid.IsFailure) return valid;
        if (ProviderSteps >= maximumSteps) return AiAssistantErrors.LimitExceeded;
        ProviderSteps++;
        UsageUnknown = true;
        Stage = "consulting";
        AdvanceRevision();
        return Result.Success();
    }

    public Result RecordProgress(long fence, DateTime nowUtc, string stage, int toolCalls, int toolBytes,
        long? inputTokens, long? outputTokens)
    {
        var valid = VerifyFence(fence, nowUtc);
        if (valid.IsFailure) return valid;
        Stage = stage;
        ToolCalls = checked(ToolCalls + toolCalls);
        ToolBytes = checked(ToolBytes + toolBytes);
        RecordUsage(inputTokens, outputTokens);
        AdvanceRevision();
        return Result.Success();
    }

    public Result Complete(long fence, string protectedAnswer, string protectedEvidences, DateTime nowUtc)
    {
        var valid = VerifyFence(fence, nowUtc);
        if (valid.IsFailure) return valid;
        ProtectedAnswer = protectedAnswer;
        ProtectedEvidences = protectedEvidences;
        SetTerminal(AiAssistantTurnState.Completed, null, nowUtc);
        return Result.Success();
    }

    public Result Fail(long fence, string code, bool uncertain, DateTime nowUtc)
    {
        if (State != AiAssistantTurnState.Running || Fence != fence) return AiAssistantErrors.FenceRejected;
        SetTerminal(CancelRequestedAtUtc is not null ? AiAssistantTurnState.Canceled : uncertain ? AiAssistantTurnState.Uncertain :
            code == AiAssistantErrors.Expired.Code && ProviderSteps == 0 ? AiAssistantTurnState.Expired : AiAssistantTurnState.Failed, code, nowUtc);
        return Result.Success();
    }

    public void RecordUsage(long? inputTokens, long? outputTokens, decimal? audioSeconds = null)
    {
        if (audioSeconds is not null) AudioSeconds = checked((AudioSeconds ?? 0) + audioSeconds.Value);
        if (inputTokens is not null) InputTokens = checked((InputTokens ?? 0) + inputTokens.Value);
        if (outputTokens is not null) OutputTokens = checked((OutputTokens ?? 0) + outputTokens.Value);
        if (inputTokens is not null && outputTokens is not null) ReportedUsageSteps++;
        UsageUnknown = ReportedUsageSteps < ProviderSteps;
    }

    public void RequestCancellation(DateTime nowUtc)
    {
        if (State is not (AiAssistantTurnState.Pending or AiAssistantTurnState.Running)) return;
        CancelRequestedAtUtc ??= nowUtc;
        if (State == AiAssistantTurnState.Pending) SetTerminal(AiAssistantTurnState.Canceled, AiAssistantErrors.Canceled.Code, nowUtc);
        else AdvanceRevision();
    }

    public void Reconcile(DateTime nowUtc, int pendingSeconds, int graceSeconds)
    {
        if (State == AiAssistantTurnState.Pending && SubmittedAtUtc.AddSeconds(pendingSeconds) <= nowUtc)
            SetTerminal(AiAssistantTurnState.Expired, AiAssistantErrors.Expired.Code, nowUtc);
        if (State == AiAssistantTurnState.Running && DeadlineAtUtc?.AddSeconds(graceSeconds) <= nowUtc)
            SetTerminal(CancelRequestedAtUtc is not null ? AiAssistantTurnState.Canceled : ProviderSteps > 0 ? AiAssistantTurnState.Uncertain : AiAssistantTurnState.Expired,
                ProviderSteps > 0 ? AiAssistantErrors.ProviderUncertain.Code : AiAssistantErrors.Expired.Code, nowUtc);
    }

    public void PurgeContent()
    {
        ProtectedQuestion = null;
        ProtectedAnswer = null;
        ProtectedEvidences = null;
        AdvanceRevision();
    }

    private void SetTerminal(AiAssistantTurnState state, string? failureCode, DateTime nowUtc)
    {
        State = state;
        Stage = "finished";
        FailureCode = failureCode;
        CompletedAtUtc = nowUtc;
        AdvanceRevision();
    }

    private void AdvanceRevision() => Revision = checked(Revision + 1);
    public override string ToString() => "AiAssistantTurn { content: [REDACTED] }";
}
