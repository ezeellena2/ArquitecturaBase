namespace ArquitecturaBase.Application.Models.StructuredData;

// RE1-A: what an external action returns for a response, such as the voucher ARCA authorized. Source is a stable lowercase key
// («arca.invoice»); names go by language (es, en). Kind is «text», «date» (yyyy-MM-dd) or «number» (canonical invariant decimal).
// The forms module keeps it sealed in the response and shows it in the detail, the sheet, the Excel, the PDF and the messages.
public sealed record StructuredRecordResultField(string Key, IReadOnlyDictionary<string, string> Label, string Kind)
{
    // H29: an amount of money is written with two fixed decimals, in the region of each language.
    public const string Text = "text", Date = "date", Number = "number", Amount = "amount";
    public override string ToString() => "StructuredRecordResultField { Content = [REDACTED] }";
}
public sealed record StructuredRecordResultValue(string Key, IReadOnlyDictionary<string, string> Label, string Kind, string? Value)
{
    public override string ToString() => "StructuredRecordResultValue { Content = [REDACTED] }";
}
public sealed record StructuredRecordResult(string Source, IReadOnlyDictionary<string, string> SourceName, IReadOnlyList<StructuredRecordResultValue> Values)
{
    public const int MaxValues = 20;
    public override string ToString() => "StructuredRecordResult { Content = [REDACTED] }";
}
