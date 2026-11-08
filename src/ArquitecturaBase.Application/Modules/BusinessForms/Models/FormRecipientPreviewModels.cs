using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormRecipientPreviewRequest(string Type, FormQuestionSource Source, Guid SampleUserId, string Culture)
{
    public override string ToString() => "FormRecipientPreviewRequest { Content = [REDACTED] }";
}

public sealed record ListFormRecipientSamplesRequest : PagedRequest
{
    public static IReadOnlyCollection<string> SortableFields { get; } = ["name"];
    public override string ToString() => "ListFormRecipientSamplesRequest { Content = [REDACTED] }";
}

public sealed record FormRecipientPreviewAttributeResponse(Guid AttributeId, string Name, string Text)
{
    public override string ToString() => "FormRecipientPreviewAttributeResponse { Content = [REDACTED] }";
}

public sealed record FormRecipientPreviewRecipientResponse(Guid Id, string Name, IReadOnlyList<FormRecipientPreviewAttributeResponse> Values)
{
    public override string ToString() => "FormRecipientPreviewRecipientResponse { Content = [REDACTED] }";
}

public sealed record FormRecipientPreviewOptionResponse(Guid Id, string Title, string? Description)
{
    public override string ToString() => "FormRecipientPreviewOptionResponse { Content = [REDACTED] }";
}

public sealed record FormRecipientPreviewResponse(FormRecipientPreviewRecipientResponse Recipient, string State, int OptionCount,
    int Limit, bool ExceedsLimit, IReadOnlyList<FormRecipientPreviewOptionResponse> Options)
{
    public override string ToString() => "FormRecipientPreviewResponse { Content = [REDACTED] }";
}

public sealed record FormRecipientMaximumRequest(FormQuestionSource Source)
{
    public override string ToString() => "FormRecipientMaximumRequest { Content = [REDACTED] }";
}

// LD1-A: a question that chooses first by a field also says how many values there are and how many options the largest has;
// both are omitted otherwise.
public sealed record FormRecipientMaximumResponse(int Maximum, string? MaximumLabel,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? Groups = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? LargestGroup = null)
{
    public override string ToString() => "FormRecipientMaximumResponse { Content = [REDACTED] }";
}
