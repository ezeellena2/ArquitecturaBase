namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// A reply button of a form action: the owner's stable id and its title, fixed with the durable message. The button sent to
/// Meta carries <see cref="ReplyId"/>, which names the outgoing row and the choice: a reply finds its row by that id and is
/// checked against the row's recipient, never trusted for who sent it.
/// </summary>
public sealed record WhatsAppFormActionChoice(string Id, string Title)
{
    public const int MaxChoices = 3;
    public const int MaxIdLength = 32;
    public const int MaxTitleLength = 20;
    public const string ReplyPrefix = "FORM_ACTION:";

    /// <summary>A tap still unhandled a day after it arrived is no longer retried.</summary>
    public static readonly TimeSpan PendingReplyWindow = TimeSpan.FromDays(1);

    /// <summary>One to three buttons with unique ids and unique visible titles of at most 20 characters, as Meta requires.</summary>
    public static bool AreValid(IReadOnlyList<WhatsAppFormActionChoice> choices) =>
        choices.Count is >= 1 and <= MaxChoices
        && choices.All(choice => IsValidId(choice.Id) && choice.Title is { Length: <= MaxTitleLength } title && WhatsAppOutgoingMessage.IsVisibleBody(title))
        && choices.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count() == choices.Count
        && choices.Select(choice => choice.Title).Distinct(StringComparer.Ordinal).Count() == choices.Count;

    /// <summary>The button id sent to Meta: the prefix, the outgoing id (32 hex) and the choice; at most 77 characters.</summary>
    public static string ReplyId(Guid outgoingId, string choiceId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(outgoingId, Guid.Empty);
        if (!IsValidId(choiceId)) throw new ArgumentException("The choice id is invalid.", nameof(choiceId));
        return $"{ReplyPrefix}{outgoingId:N}:{choiceId}";
    }

    public static bool TryParseReplyId(string? value, out Guid outgoingId, out string choiceId)
    {
        outgoingId = Guid.Empty;
        choiceId = string.Empty;
        if (value is null || value.Length <= ReplyPrefix.Length + 33 || !value.StartsWith(ReplyPrefix, StringComparison.Ordinal)) return false;
        var content = value.AsSpan(ReplyPrefix.Length);
        var choice = content[33..].ToString();
        if (content[32] != ':' || !Guid.TryParseExact(content[..32], "N", out var outgoing) || outgoing == Guid.Empty || !IsValidId(choice)) return false;
        outgoingId = outgoing;
        choiceId = choice;
        return true;
    }

    private static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= MaxIdLength && id == id.Trim() && !id.Any(char.IsControl);

    public override string ToString() => "WhatsAppFormActionChoice { Content = [REDACTED] }";
}
