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

/// <summary>
/// The agent's limits. <see cref="DailyOfferLimit"/> (TO1-A, 2026-11-21) is how many forms the assistant offers on its own per
/// conversation and business day. RC2-B (2026-11-22) adds how many forms a spreadsheet sends (<see cref="MaxSpreadsheetLoads"/>, the
/// rest goes to a person), how many pages of a PDF are read (<see cref="MaxDocumentPages"/>) and after how many days a declined form
/// may be offered again (<see cref="DeclinedReofferDays"/>, null: never). Each new limit goes last with its default, so that limits
/// saved before it, or by the previous version while both run, read the value of before.
/// </summary>
public sealed record WhatsAppAssistantLimits(IReadOnlyList<string> BlockedTopics, string CannotPromise, int MaxReplyLength, int DailyReplyLimit,
    IReadOnlyList<WhatsAppAssistantActionKind> AllowedActions, int DailyOfferLimit = WhatsAppAssistantAgentLimits.DefaultDailyOffers,
    int MaxSpreadsheetLoads = WhatsAppAssistantAgentLimits.DefaultSpreadsheetLoads, int MaxDocumentPages = WhatsAppAssistantAgentLimits.DefaultDocumentPages,
    int? DeclinedReofferDays = null)
{
    /// <summary>
    /// From when a declined form keeps the assistant from sending it again: null, every decline of the conversation's link (never again);
    /// with <see cref="DeclinedReofferDays"/>, only the declines of the last days.
    /// </summary>
    public DateTime? DeclinedSinceUtc(DateTime nowUtc) => DeclinedReofferDays is { } days ? nowUtc.AddDays(-days) : null;
}

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
    public const int MinDailyOffers = 1;
    public const int MaxDailyOffers = 50;
    public const int DefaultDailyOffers = 2;
    public const int MinSpreadsheetLoads = 1;

    /// <summary>As many loads as a spreadsheet can carry (<see cref="WhatsAppAssistantRequest.MaxLoads"/>).</summary>
    public const int MaxSpreadsheetLoads = WhatsAppAssistantRequest.MaxLoads;

    public const int DefaultSpreadsheetLoads = 3;
    public const int MinDocumentPages = 1;

    /// <summary>As many pages as the documents' reader and the engine take (<see cref="WhatsAppMessage.MaxDocumentPages"/>).</summary>
    public const int MaxDocumentPages = WhatsAppMessage.MaxDocumentPages;

    public const int DefaultDocumentPages = MaxDocumentPages;
    public const int MinDeclinedReofferDays = 1;
    public const int MaxDeclinedReofferDays = 365;

    /// <summary>
    /// Until the profile is saved: every action allowed, 400 characters, 10 replies and 2 form offers per conversation and day, 3 forms
    /// per spreadsheet, 10 pages per PDF and a declined form never offered again (AS5, TO1-A, RC2-B).
    /// </summary>
    public static WhatsAppAssistantLimits DefaultLimits { get; } = new([], "", 400, 10, Enum.GetValues<WhatsAppAssistantActionKind>(), DefaultDailyOffers,
        DefaultSpreadsheetLoads, DefaultDocumentPages);
}
