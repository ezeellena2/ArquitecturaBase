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
    int Limit, bool ExceedsLimit, IReadOnlyList<FormRecipientPreviewOptionResponse> Options, FormCatalogPresentationCheck PresentationCheck)
{
    public override string ToString() => "FormRecipientPreviewResponse { Content = [REDACTED] }";
}
