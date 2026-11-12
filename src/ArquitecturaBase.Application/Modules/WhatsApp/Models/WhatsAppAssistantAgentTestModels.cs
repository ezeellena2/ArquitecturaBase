using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// Probar (AS3): the messages someone writes as an account linked to WhatsApp, to see what the assistant would do with what is
/// saved and that account's real history. Nothing is queued and no limit of WhatsApp is spent.
/// </summary>
public sealed record StartWhatsAppAssistantAgentTestRequest(Guid RequestId, Guid ContactId, IReadOnlyList<string> Messages)
{
    public const int MaxMessages = 5;
    public const int MaxMessageLength = 1000;

    public override string ToString() => "StartWhatsAppAssistantAgentTestRequest { Content = [REDACTED] }";
}

public sealed record WhatsAppAssistantAgentTestAcceptedResponse(Guid RequestId, string State);

/// <summary>What the assistant would do: pending, completed with its actions in order, or failed with the code of why.</summary>
public sealed record WhatsAppAssistantAgentTestResponse(Guid RequestId, string State, string? FailureCode,
    IReadOnlyList<WhatsAppAssistantAgentTestAction> Actions)
{
    public override string ToString() => "WhatsAppAssistantAgentTestResponse { Content = [REDACTED] }";
}

/// <summary>
/// An action as a conversation would get it: the case («Cuándo», the form's name or «Cualquier otra cosa» with null), why, what it
/// used, the text that would go out, the buttons or the form with what it would fill, and the reason of a handoff.
/// </summary>
public sealed record WhatsAppAssistantAgentTestAction(WhatsAppAssistantActionKind Kind, WhatsAppAssistantCaseSource CaseSource, string? CaseTitle,
    string Reason, IReadOnlyList<string> Used, string? Text, IReadOnlyList<string>? Buttons, WhatsAppAssistantAgentTestForm? Form,
    string? HandoffReasonCode, string? HandoffReason)
{
    public override string ToString() => "WhatsAppAssistantAgentTestAction { Content = [REDACTED] }";
}

public sealed record WhatsAppAssistantAgentTestForm(string Name, IReadOnlyList<WhatsAppAssistantAgentTestField> Fields)
{
    public override string ToString() => "WhatsAppAssistantAgentTestForm { Content = [REDACTED] }";
}

public sealed record WhatsAppAssistantAgentTestField(string Label, string? Value)
{
    public override string ToString() => "WhatsAppAssistantAgentTestField { Content = [REDACTED] }";
}

/// <summary>An account Probar can write as: linked to a WhatsApp chat and active.</summary>
public sealed record WhatsAppAssistantTestRecipientRow(Guid ContactId, string Name, string? Email)
{
    public override string ToString() => "WhatsAppAssistantTestRecipientRow { Recipient = [REDACTED] }";
}

/// <summary>The account's email only goes with the permission to read accounts.</summary>
public sealed record WhatsAppAssistantTestRecipientsResponse(IReadOnlyList<WhatsAppAssistantTestRecipientRow> Items);
