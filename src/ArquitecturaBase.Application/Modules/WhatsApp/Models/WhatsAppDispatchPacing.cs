namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// The pace of the outgoing to one person: after a send finishes, the next message to them waits six seconds. The lease in Redis
/// keeps it across hosts and the choice of the next chat skips whoever is still waiting (H12).
/// </summary>
public static class WhatsAppDispatchPacing
{
    public static readonly TimeSpan RecipientWait = TimeSpan.FromSeconds(6);

    /// <summary>H7: how long a send that did not go out (the network or Meta refused it before it arrived) waits to try again.</summary>
    public static readonly TimeSpan RetryWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// H7: a send that did not go out is retried at most twice, three attempts as in the access queue; the third refusal fails it
    /// like any other, so a long outage does not hold the chat (and the operator) for hours.
    /// </summary>
    public const int MaxRetries = 2;

    /// <summary>
    /// H25: how long since its request a send of a kind this version does not know waits for a copy that knows it, as long as a
    /// gradual deployment; then it fails as one that could not go out.
    /// </summary>
    public static readonly TimeSpan AnotherCopyLimit = TimeSpan.FromMinutes(10);
}
