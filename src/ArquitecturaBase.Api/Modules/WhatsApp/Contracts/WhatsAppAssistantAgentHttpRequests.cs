using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Contracts;

/// <summary>Casos del asistente (AS1-A): the four tabs in one save, with the revision read (0 before the first save).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateWhatsAppAssistantAgentHttpRequest(long ExpectedRevision, string OriginalCulture, WhatsAppAssistantProfileHttpRequest Profile,
    IReadOnlyList<WhatsAppAssistantCaseHttpRequest> Cases, WhatsAppAssistantActionHttpRequest Fallback, WhatsAppAssistantLimitsHttpRequest Limits)
{
    public override string ToString() => "UpdateWhatsAppAssistantAgentHttpRequest { Content = [REDACTED] }";
}

/// <summary>A fixed text in the agent's original language and, optionally, the other one.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WhatsAppAssistantTextHttpRequest(string? Es, string? En)
{
    public override string ToString() => "WhatsAppAssistantTextHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WhatsAppAssistantProfileHttpRequest(string Name, string Introduction, string Tone, string Does, string DoesNot,
    WhatsAppAssistantTextHttpRequest HandoffMessage)
{
    public override string ToString() => "WhatsAppAssistantProfileHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WhatsAppAssistantCaseHttpRequest(Guid Id, string When, WhatsAppAssistantInputType InputType, bool IsActive,
    WhatsAppAssistantActionHttpRequest Action)
{
    public override string ToString() => "WhatsAppAssistantCaseHttpRequest { Content = [REDACTED] }";
}

/// <summary>An action of the closed menu with only what it uses; whatever it does not use goes null.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WhatsAppAssistantActionHttpRequest(WhatsAppAssistantActionKind Kind, Guid? FormDefinitionId = null,
    WhatsAppAssistantTextHttpRequest? Text = null, string? Instructions = null, WhatsAppAssistantTextHttpRequest? Question = null,
    IReadOnlyList<WhatsAppAssistantButtonHttpRequest>? Buttons = null, string? HandoffReason = null)
{
    public override string ToString() => "WhatsAppAssistantActionHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WhatsAppAssistantButtonHttpRequest(WhatsAppAssistantTextHttpRequest Title, WhatsAppAssistantButtonDestination Destination,
    Guid? FormDefinitionId = null, WhatsAppAssistantTextHttpRequest? Text = null)
{
    public override string ToString() => "WhatsAppAssistantButtonHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WhatsAppAssistantLimitsHttpRequest(IReadOnlyList<string> BlockedTopics, string CannotPromise, int MaxReplyLength,
    int DailyReplyLimit, IReadOnlyList<WhatsAppAssistantActionKind> AllowedActions)
{
    public override string ToString() => "WhatsAppAssistantLimitsHttpRequest { Content = [REDACTED] }";
}

/// <summary>Probar (AS3): the messages the chosen account would write, one to five.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StartWhatsAppAssistantAgentTestHttpRequest(Guid RequestId, Guid ContactId, IReadOnlyList<string> Messages)
{
    public override string ToString() => "StartWhatsAppAssistantAgentTestHttpRequest { Content = [REDACTED] }";
}

/// <summary>Probar: the chat of the account the test wrote as.</summary>
public sealed record WhatsAppAssistantAgentTestQuery(Guid ContactId);

/// <summary>Probar: the accounts to write as, by name or email.</summary>
public sealed record WhatsAppAssistantTestRecipientsQuery(string? Search = null)
{
    public override string ToString() => "WhatsAppAssistantTestRecipientsQuery { Search = [REDACTED] }";
}

/// <summary>By hand, keeping the nulls: the validator answers at their path.</summary>
internal static class WhatsAppAssistantAgentRequestMapping
{
    public static UpdateWhatsAppAssistantAgentRequest Map(UpdateWhatsAppAssistantAgentHttpRequest request) => new(request.ExpectedRevision,
        request.OriginalCulture, Map(request.Profile)!, request.Cases?.Select(Map).ToArray()!, Map(request.Fallback)!, Map(request.Limits)!);

    private static WhatsAppAssistantProfile? Map(WhatsAppAssistantProfileHttpRequest? profile) => profile is null ? null
        : new(profile.Name, profile.Introduction, profile.Tone, profile.Does, profile.DoesNot, Map(profile.HandoffMessage)!);

    private static WhatsAppAssistantCase Map(WhatsAppAssistantCaseHttpRequest? item) => item is null ? null!
        : new(item.Id, item.When, item.InputType, item.IsActive, Map(item.Action)!);

    private static WhatsAppAssistantAction? Map(WhatsAppAssistantActionHttpRequest? action) => action is null ? null
        : new(action.Kind, action.FormDefinitionId, Map(action.Text), action.Instructions, Map(action.Question),
            action.Buttons?.Select(button => button is null ? null! : new WhatsAppAssistantButton(Map(button.Title)!, button.Destination,
                button.FormDefinitionId, Map(button.Text))).ToArray(), action.HandoffReason);

    private static WhatsAppAssistantLimits? Map(WhatsAppAssistantLimitsHttpRequest? limits) => limits is null ? null
        : new(limits.BlockedTopics, limits.CannotPromise, limits.MaxReplyLength, limits.DailyReplyLimit, limits.AllowedActions);

    private static WhatsAppAssistantText? Map(WhatsAppAssistantTextHttpRequest? text) => text is null ? null : new(text.Es, text.En);
}
