namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>
/// F28: the kinds of action this version knows. The rule editor accepts only these, and a worker reserves only these, so that in a
/// rolling deploy that adds a kind the older copy leaves the new actions for the new one instead of failing them.
/// </summary>
public static class FormActionKinds
{
    public static readonly IReadOnlyList<string> Known = ["RequestApproval", "GeneratePdf", "NotifyAuthor", "NotifyRole", "IssueDocument", "SendCopy"];
}
