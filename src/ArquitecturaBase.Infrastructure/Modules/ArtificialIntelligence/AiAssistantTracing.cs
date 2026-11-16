using System.Diagnostics;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;

/// <summary>
/// I10: the assistant's traces (its <c>AddSource</c> is in the module's registration), as WhatsApp's H19. A turn keeps the W3C
/// context of whoever submitted it (the request in Home, the round in a channel), and the coordinator continues it with one activity
/// per turn around its capabilities and the provider call. Tags carry only identifiers and codes: the turn, its channel, purpose,
/// provider and capabilities, never a question, an answer, a file name or the text of a provider's error.
/// </summary>
internal static class AiAssistantTracing
{
    public const string SourceName = "ArquitecturaBase.ArtificialIntelligence";

    private static readonly ActivitySource Source = new(SourceName);

    /// <summary>The context to keep in a turn: only a W3C one fits its column.</summary>
    public static string? Current() => Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

    /// <summary>A stored context that cannot be read is ignored: the turn starts its own trace.</summary>
    public static Activity? StartTurn(Guid turnId, string? traceParent)
    {
        var activity = Source.StartActivity("ai.assistant.turn", ActivityKind.Consumer,
            ActivityContext.TryParse(traceParent, null, isRemote: true, out var parent) ? parent : default);
        activity?.SetTag("ai.turn.id", turnId.ToString());
        return activity;
    }

    public static void Describe(Activity? activity, AiAssistantExecutionContextResponse context)
    {
        if (activity is null) return;
        activity.SetTag("ai.provider", context.ProviderRequest.Provider.ToString());
        if (context.ExecutionProfile is not { } profile) return;
        activity.SetTag("ai.channel", profile.ChannelContext.ChannelId);
        activity.SetTag("ai.purpose", profile.Purpose.ToString());
        activity.SetTag("ai.capabilities", profile.ChannelContext.AllowedCapabilities
            .Select(capability => $"{capability.Id}@{capability.Version}").ToArray());
    }

    /// <summary>
    /// Marks the turn's activity by what was saved: a turn that ended other than completed, also one the engine closed on its own (a step
    /// limit, lost access, an answer it could not verify), is an error with its code; one not yet finished is left as it is.
    /// </summary>
    public static void MarkOutcome(Activity? activity, AiAssistantTurnState state, string? failureCode)
    {
        if (activity is null || state is AiAssistantTurnState.Pending or AiAssistantTurnState.Running or AiAssistantTurnState.Completed) return;
        activity.SetStatus(ActivityStatusCode.Error, failureCode);
    }
}
