using System.Collections.Frozen;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Configuration;

public static class WhatsAppFlowProfile
{
    // r10 adds the block of rows (a screen per row); without a block it compiles the same bytes as r9. r11 adds the question
    // that chooses first by a field (LD2-A, a dropdown of values before its list); without one it compiles the same bytes as r10.
    // r12 ends a block with nothing after it on a closing screen, because Meta rejects a row that may continue and also complete
    // (2026-11-09); any other form compiles the same bytes as r11. r13 leaves a calculation out of the Flow (CA1-A) and puts a
    // question with a condition inside an If on the answer of its question (CO1-A); without them it compiles the same bytes as r12.
    // r14 (task 9b of the documents) opens a form that reads documents with what was read: a hidden notice atop the first screen, the
    // dynamic help of each text or date control without its own, the dynamic error-messages of each Form and, for the question that
    // may receive the sent document on the last screen, a twin screen chosen by an If (the notice and an optional picker); without
    // reading it compiles the same bytes as r13.
    public const string Revision = "flow-static-7.3-r14";
    public static IReadOnlySet<string> PrefillRevisions { get; } = new[]
    {
        "flow-static-7.3-r8", "flow-static-7.3-r9", "flow-static-7.3-r10", "flow-static-7.3-r11", "flow-static-7.3-r12",
        "flow-static-7.3-r13", "flow-static-7.3-r14"
    }.ToFrozenSet(StringComparer.Ordinal);
    // The rows r10 to r14 compile, prefill and map from their sealed bytes: another cap is another revision, and the webhook
    // keeps accepting _r10 from the flows already published. Publishing warns with the same number (StructuredFormLimits).
    public const int MaxRows = 10;
    public const string GraphApiVersion = "v25.0";
    public const int CodecVersion = 1;
    public const int MaxResponseJsonBytes = 16 * 1024;
    public const int MaxMediaResponseJsonBytes = 64 * 1024;
    public const string ResponseErrorProperty = "_flow_error";
    public const string ResponseTooLargeError = "ResponseTooLarge";
    public const string DecimalPattern = "^-?(0|[1-9][0-9]*)([.,][0-9]+)?$";
    public const int DecimalMaxChars = 60;
}
