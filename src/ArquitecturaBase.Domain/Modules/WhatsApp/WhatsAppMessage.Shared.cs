using System.Text.Json;
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

// W3: what the person shares while a form is open is kept once with its message, as it came, and goes with the body when the history is
// cleared: a place's address with its point, and the formatted name and the phones of a contact's first card. A resend of a form (task
// 23) is marked on the message it leaves when it goes out.
public sealed partial class WhatsAppMessage
{
    /// <summary>The longest address of a shared place kept; a longer one is cut, like a text the person chose.</summary>
    public const int MaxLocationAddressLength = 256;

    /// <summary>The longest formatted name of a shared contact kept, as long as a profile name.</summary>
    public const int MaxContactNameLength = WhatsAppContact.MaxProfileNameLength;

    /// <summary>The longest phone of a shared contact kept, with its spaces and signs; a longer one is no phone and is left out.</summary>
    public const int MaxContactPhoneLength = 64;

    /// <summary>How many phones of a shared contact are kept, the first ones in the card's order.</summary>
    public const int MaxContactPhones = 10;

    /// <summary>The address of a shared place, when Meta sends one; the name of the place is not kept.</summary>
    public string? LocationAddress { get; private set; }

    /// <summary>The formatted name of the first card of a shared contact; null when it brings none, never empty.</summary>
    public string? ContactName { get; private set; }

    /// <summary>
    /// The phones of the first card of a shared contact, a JSON array of up to ten as they came: <c>[]</c> when it brings none, so a
    /// recorded card is never null until the history is cleared.
    /// </summary>
    public string? ContactPhonesJson { get; private set; }

    /// <summary>A resend of the form of a send, with what was shared over its prefill (task 23); false in every message of before.</summary>
    public bool IsResend { get; private set; }

    /// <summary>
    /// W3: the formatted name and the phones of the first card of an inbound contact, once. The name is kept like a profile name (null
    /// when blank) and each phone as it came, trimmed: a blank one, one longer than any number or one with a control character is left
    /// out, never cut, and the first ten that are phones stay.
    /// </summary>
    public void RecordContact(string? name, IEnumerable<string?> phones)
    {
        ArgumentNullException.ThrowIfNull(phones);
        if (Kind != WhatsAppMessageKind.Contact || Direction != WhatsAppMessageDirection.Inbound || ContactPhonesJson is not null)
            throw new InvalidOperationException("Only an inbound contact records its first card, once.");
        ContactName = SharedText(name, MaxContactNameLength);
        ContactPhonesJson = JsonSerializer.Serialize(phones.Select(phone => phone?.Trim()).OfType<string>().Where(IsContactPhone).Take(MaxContactPhones).ToArray());
    }

    /// <summary>W3: the Flow that went out as the resend of its send keeps the mark, once; it is not the one «Por qué lo mandó» goes with.</summary>
    public void MarkResend()
    {
        if (Direction != WhatsAppMessageDirection.Outbound || Kind != WhatsAppMessageKind.Flow || FlowSessionId is null || IsResend)
            throw new InvalidOperationException("Only a sent Flow of a send is marked as its resend, once.");
        IsResend = true;
    }

    private static bool IsContactPhone(string phone) =>
        phone.Length is > 0 and <= MaxContactPhoneLength && StorableText.IsStorable(phone) && !phone.Any(char.IsControl);

    // A text another person chose (a place's address, a contact's name) is cleaned and cut instead of refusing the message.
    private static string? SharedText(string? text, int maxLength) =>
        text is null ? null : StorableText.Clean(StorableText.Sanitize(text).Trim(), maxLength);
}
