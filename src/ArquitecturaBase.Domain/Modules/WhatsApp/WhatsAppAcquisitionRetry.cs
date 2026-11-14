namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// H11: how long a file received by WhatsApp (the files of a Flow reply, an audio, a spreadsheet, a received document) keeps
/// retrying while Meta, the token or the private storage fail. It gives up only with at least five failures and a day since the
/// first one; meanwhile the count keeps growing, so the readers' wait between attempts grows up to an hour and the processor does
/// not go back to Meta in each round. Past the usual window (five failures and fifteen minutes) it no longer holds back what it
/// pairs with: a text, or a spreadsheet after an audio, goes on as with a file that is not available.
/// </summary>
public static class WhatsAppAcquisitionRetry
{
    public const int MinimumFailures = 5;

    /// <summary>The count stops growing here, far past the day: giving up comes near the thirty-first failure.</summary>
    public const int MaxCountedFailures = 64;

    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromDays(1);

    /// <summary>How long a file that keeps failing holds back what it pairs with: the window it had before it retried for a day.</summary>
    public static readonly TimeSpan HoldFor = TimeSpan.FromMinutes(15);

    /// <summary>An attempt due this soon keeps the processor in its quick rounds; a later one waits for its usual round.</summary>
    public static readonly TimeSpan ImminentAttempt = TimeSpan.FromSeconds(30);

    public static int Count(int failures) => Math.Min(failures + 1, MaxCountedFailures);

    public static bool GivesUp(int failures, DateTime firstFailureAtUtc, DateTime nowUtc) =>
        failures >= MinimumFailures && nowUtc - firstFailureAtUtc >= GiveUpAfter;

    public static bool StopsHolding(int failures, DateTime? firstFailureAtUtc, DateTime nowUtc) =>
        failures >= MinimumFailures && firstFailureAtUtc is { } first && nowUtc - first >= HoldFor;
}
