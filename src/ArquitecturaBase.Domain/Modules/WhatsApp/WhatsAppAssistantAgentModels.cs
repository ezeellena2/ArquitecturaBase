namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>What a case reacts to. Photos and documents are the next stage; the list leaves room for them.</summary>
public enum WhatsAppAssistantInputType { Any, Text, Audio }

/// <summary>«Qué hacer», the closed menu: the only part of the cases that lives in the code (AS2).</summary>
public enum WhatsAppAssistantActionKind { SendForm, ExactText, DraftedReply, AskButtons, Handoff, NoReply }

/// <summary>Where a button of «Preguntar con botones» leads.</summary>
public enum WhatsAppAssistantButtonDestination { SendForm, ExactText, Handoff }

/// <summary>
/// A fixed text that goes out as written: in the agent's original language and, optionally, its translation (the language of
/// the menu, as in forms). A conversation in the other language without a translation gets the original.
/// </summary>
public sealed record WhatsAppAssistantText(string? Es, string? En)
{
    public string? In(string culture) => culture == "en" ? En : Es;

    public string? For(string culture, string originalCulture) => In(culture) ?? In(originalCulture);
}

public sealed record WhatsAppAssistantButton(WhatsAppAssistantText Title, WhatsAppAssistantButtonDestination Destination,
    Guid? FormDefinitionId, WhatsAppAssistantText? Text);

/// <summary>An action of the menu with only what it uses: the form, the exact text, the instructions, the buttons or the reason.</summary>
public sealed record WhatsAppAssistantAction(WhatsAppAssistantActionKind Kind, Guid? FormDefinitionId = null, WhatsAppAssistantText? Text = null,
    string? Instructions = null, WhatsAppAssistantText? Question = null, IReadOnlyList<WhatsAppAssistantButton>? Buttons = null, string? HandoffReason = null);

/// <summary>An own case: when (free text the assistant reads to decide), the input it reacts to, whether it is active and what it does.</summary>
public sealed record WhatsAppAssistantCase(Guid Id, string When, WhatsAppAssistantInputType InputType, bool IsActive, WhatsAppAssistantAction Action);

public sealed record WhatsAppAssistantProfile(string Name, string Introduction, string Tone, string Does, string DoesNot,
    WhatsAppAssistantText HandoffMessage);

public sealed record WhatsAppAssistantLimits(IReadOnlyList<string> BlockedTopics, string CannotPromise, int MaxReplyLength, int DailyReplyLimit,
    IReadOnlyList<WhatsAppAssistantActionKind> AllowedActions);

/// <summary>The limits of the agent's configuration: every layer uses these, from the validator to the screen.</summary>
public static class WhatsAppAssistantAgentLimits
{
    public const int NameLength = 80;
    public const int IntroductionLength = 500;
    public const int ToneLength = 200;
    public const int DoesLength = 1000;
    public const int HandoffMessageLength = 1000;
    public const int Cases = 20;
    public const int WhenLength = 300;
    public const int ExactTextLength = 1000;
    public const int InstructionsLength = 1000;

    /// <summary>The body of a message with buttons, as WhatsApp allows.</summary>
    public const int QuestionLength = 1024;

    public const int Buttons = 3;
    public const int ButtonTitleLength = 20;
    public const int HandoffReasonLength = WhatsAppContact.MaxHandoffReasonLength;
    public const int BlockedTopics = 20;
    public const int BlockedTopicLength = 80;
    public const int CannotPromiseLength = 500;
    public const int MinReplyLength = 100;
    public const int MaxReplyLength = 1000;
    public const int MinDailyReplies = 1;
    public const int MaxDailyReplies = 50;

    /// <summary>Until the profile is saved: every action allowed, 400 characters and 10 replies per conversation and day (AS5).</summary>
    public static WhatsAppAssistantLimits DefaultLimits { get; } = new([], "", 400, 10, Enum.GetValues<WhatsAppAssistantActionKind>());
}
