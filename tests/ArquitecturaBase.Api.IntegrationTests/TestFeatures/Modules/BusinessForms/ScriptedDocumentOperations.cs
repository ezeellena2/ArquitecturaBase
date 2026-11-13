using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// Task 7: the engine for changing a received document's form, scripted: it keeps what the inbox submits, by who asked and its request,
// and answers it as pending until the test completes it with the operation the real engine would return. Like the engine, an operation
// is found only with the context it was sealed with (another reviewer, or another language, does not find it), and a submission or a read
// can fail as an engine that refuses or is not available would. It never calls a provider and qualifies no model.
internal sealed class ScriptedDocumentOperations : IAssistantOperations
{
    private readonly ConcurrentDictionary<(Guid Actor, Guid RequestId), AssistantOperationResponse> completed = new();
    public ConcurrentQueue<AssistantOperationRequest> Submitted { get; } = new();
    public ConcurrentQueue<AssistantChannelContext> Read { get; } = new();
    /// <summary>When set, every submission fails with it.</summary>
    public Error? SubmitFailure { get; set; }
    /// <summary>When set, every read fails with it: a refusal for good (forbidden) or a transient failure.</summary>
    public Error? ReadFailure { get; set; }
    /// <summary>When set, the next submission throws it, once: a step that fails halfway (the database or the network).</summary>
    public Exception? ThrowNext { get => Volatile.Read(ref throwNext); set => Volatile.Write(ref throwNext, value); }
    /// <summary>How many submissions it was asked for, taken, failed or thrown.</summary>
    public int Attempts => Volatile.Read(ref attempts);
    private int attempts;
    private Exception? throwNext;

    public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new AssistantAvailabilityResponse("available"));

    public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref attempts);
        if (Interlocked.Exchange(ref throwNext, null) is { } thrown) return Task.FromException<Result<AssistantOperationAcceptedResponse>>(thrown);
        if (SubmitFailure is { } failure) return Task.FromResult(Result.Failure<AssistantOperationAcceptedResponse>(failure));
        Submitted.Enqueue(request);
        return Task.FromResult(Result.Success(new AssistantOperationAcceptedResponse(request.RequestId, "pending")));
    }

    public Task<Result> CancelAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

    public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken)
    {
        Read.Enqueue(context);
        if (ReadFailure is { } failure) return Task.FromResult(Result.Failure<AssistantOperationResponse>(failure));
        var submitted = Submitted.FirstOrDefault(request => request.RequestId == requestId);
        if (submitted is null || !SameScope(context, submitted.Context))
            return Task.FromResult(Result.Failure<AssistantOperationResponse>(FormReceivedDocumentErrors.ExtractionNotFound));
        return Task.FromResult(Result.Success(completed.TryGetValue((context.ActorUserId, requestId), out var response) ? response
            : new AssistantOperationResponse(requestId, "pending", null, null, null)));
    }

    /// <summary>The operation the engine finished for the request of whoever asked.</summary>
    public void Complete(Guid actorUserId, AssistantOperationResponse response) => completed[(actorUserId, response.RequestId)] = response;

    private static bool SameScope(AssistantChannelContext requested, AssistantChannelContext sealedContext) =>
        requested.ChannelId == sealedContext.ChannelId && requested.ActorUserId == sealedContext.ActorUserId && requested.SubjectUserId == sealedContext.SubjectUserId
        && requested.Culture == sealedContext.Culture && requested.TimeZoneId == sealedContext.TimeZoneId && requested.FocusDefinitionId == sealedContext.FocusDefinitionId;
}
