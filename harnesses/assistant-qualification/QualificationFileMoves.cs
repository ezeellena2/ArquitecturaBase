namespace AssistantQualification;

// Moves a file the harness has just closed: the ledger replaces its entry and the private audio is written once.
// On Windows a scanner or indexer may still hold a file it saw closed, without sharing delete: moving or replacing it
// is denied for a moment (2026-11-08, a live run stopped there). That denial is retried for up to ten seconds.
internal static class QualificationFileMoves
{
    private const int Attempts = 40;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    public static async Task<bool> MoveAsync(string source, string target, bool overwrite, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { File.Move(source, target, overwrite); return true; }
            catch (IOException) when (!overwrite && File.Exists(target)) { return false; }
            catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempt < Attempts)
            {
                await Task.Delay(Pause, cancellationToken);
            }
        }
    }
}
