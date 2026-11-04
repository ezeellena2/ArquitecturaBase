using System.Collections.Frozen;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Configuration;

public static class WhatsAppFlowProfile
{
    // r10 adds the block of rows (a screen per row); without a block it compiles the same bytes as r9.
    public const string Revision = "flow-static-7.3-r10";
    public static IReadOnlySet<string> PrefillRevisions { get; } = new[]
    {
        "flow-static-7.3-r8", "flow-static-7.3-r9", "flow-static-7.3-r10"
    }.ToFrozenSet(StringComparer.Ordinal);
    public const string GraphApiVersion = "v25.0";
    public const int CodecVersion = 1;
    public const int MaxResponseJsonBytes = 16 * 1024;
    public const int MaxMediaResponseJsonBytes = 64 * 1024;
    public const string ResponseErrorProperty = "_flow_error";
    public const string ResponseTooLargeError = "ResponseTooLarge";
    public const string DecimalPattern = "^-?(0|[1-9][0-9]*)([.,][0-9]+)?$";
    public const int DecimalMaxChars = 60;
}
