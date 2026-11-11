namespace ArquitecturaBase.Application.Models.StructuredData;

// BI1-A: a rule a standard of the library brings, in the forms module's rule format with the local ids of its draft: the event
// («Received», «Approved» or «Rejected»), its conditions (all of them must hold) and its actions, in order. «Agregar» leaves the
// rules as the first revision of the copy's rules, in the same save, with the identities that save gives.
public sealed record StructuredFormStandardRule(string Event, IReadOnlyList<StructuredFormStandardCondition> Conditions,
    IReadOnlyList<StructuredFormStandardAction> Actions)
{
    public override string ToString() => "StructuredFormStandardRule { Content = [REDACTED] }";
}
// A condition on a question of the draft, by its local id, with the operator and values of the rules: an option of a catalog
// question is named as «code:<code>» and one of the question's own options, by its local id.
public sealed record StructuredFormStandardCondition(string QuestionId, string Operator, IReadOnlyList<string>? Values = null)
{
    public override string ToString() => "StructuredFormStandardCondition { Content = [REDACTED] }";
}
// An action by its kind, as the rules write it: «IssueDocument» with its IssuerId; «NotifyAuthor» with its text by language
// (the original's at least) and IncludePdf; «SendCopy», the copy of an issued document to its receiver, with its text. A text may
// use the fixed data and those of an earlier result («{result:<source>.<key>}»), not questions.
public sealed record StructuredFormStandardAction(string Kind, IReadOnlyDictionary<string, string>? Text = null, bool IncludePdf = false,
    string? IssuerId = null)
{
    public override string ToString() => "StructuredFormStandardAction { Content = [REDACTED] }";
}
