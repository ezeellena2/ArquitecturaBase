using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormRecipientPreviewHttpRequest(string Type, FormQuestionSourceHttpRequest? Source, Guid SampleUserId, string Culture)
{
    public override string ToString() => "FormRecipientPreviewHttpRequest { Content = [REDACTED] }";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormRecipientMaximumHttpRequest(FormQuestionSourceHttpRequest? Source)
{
    public override string ToString() => "FormRecipientMaximumHttpRequest { Content = [REDACTED] }";
}

public sealed record FormRecipientSamplesQuery(int Page = 1, int PageSize = 20, string? Search = null)
{
    public override string ToString() => "FormRecipientSamplesQuery { Content = [REDACTED] }";
}
