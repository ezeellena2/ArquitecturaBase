using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts.Documents;

/// <summary>
/// «Armar formulario con la IA» (DOC6): the request's UUID (its repetition answers the same) and either one unclassified document or the
/// topic of a group of similar ones.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestFormDocumentProposalHttpRequest(Guid? RequestId, Guid? DocumentId, Guid? TopicId);

/// <summary>
/// «Crear borrador» of DOC6: the request and the proposal it closed on, and the form as the reviewer edited it (name, prefix, final button,
/// «Cómo reconocerlo» and the questions as the proposal names them).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFormDocumentDraftHttpRequest(Guid? RequestId, Guid? ProposalId, string? Name, string? Prefix, string? SubmitLabel,
    string? Recognition, IReadOnlyList<FormDocumentDraftQuestionHttpItem?>? Questions)
{
    public override string ToString() => "CreateFormDocumentDraftHttpRequest { Content = [REDACTED] }";
}

/// <summary>
/// A question of the edited form: its text, its type (<c>shortText</c>, <c>longText</c>, <c>number</c>, <c>date</c>, <c>yesNo</c>,
/// <c>choice</c>, <c>multiChoice</c>, <c>catalog</c>, <c>photo</c> or <c>document</c>), whether it is required, its own options (to choose)
/// or its catalog.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormDocumentDraftQuestionHttpItem(string? Label, string? Type, bool? Required, IReadOnlyList<string?>? Options, Guid? CatalogId)
{
    public override string ToString() => "FormDocumentDraftQuestionHttpItem { Content = [REDACTED] }";
}
