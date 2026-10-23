using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

internal static class FormDraftRequestMapping
{
    public static SaveFormDraftRequest Map(SaveFormDraftHttpRequest request) => new(request.RequestId, request.Name, request.Prefix,
        Map(request.Draft), request.ExpectedRevision, request.OfferWhen);

    public static CheckFormPublicationRequest Map(CheckFormPublicationHttpRequest request) => new(request.ExpectedRevision,
        request.Name, request.Prefix, Map(request.Draft));

    public static AcceptFormPublicationRequest Map(AcceptFormPublicationHttpRequest request) => new(request.RequestId,
        request.ExpectedRevision, request.CheckToken, request.Name, request.Prefix, Map(request.Draft));

    public static FormDraftInput Map(FormDraftHttpRequest? draft) => draft is null ? null! : new(draft.Format, draft.Culture,
        draft.SubmitLabel, draft.Fields?.Select(field => field is null ? null! : new FormFieldInput(field.LocalId,
            field.FieldId, field.ReplacesFieldId, field.Type, field.Label, field.Help, field.Required, field.MaxLength,
            field.Options?.Select(option => option is null ? null! : new FormChoiceInput(option.LocalId, option.ChoiceId, option.Label)).ToArray(),
            Map(field.Source),
            field.Files is null ? null : new FormFileSettings(field.Files.MinFiles, field.Files.MaxFiles, field.Files.PhotoSource),
            field.Selection is null ? null : new FormSelectionSettings(field.Selection.MinSelected, field.Selection.MaxSelected))).ToArray()!);
    public static FormQuestionSource? Map(FormQuestionSourceHttpRequest? source) => source is null ? null : new(source.Catalog,
        source.Match?.Select(match => match is null ? null! : new FormCatalogMatch(match.FieldId, match.Value, match.PersonAttributeId)).ToArray(),
        source.Presentation is null ? null : new FormCatalogPresentation(source.Presentation.Title!, source.Presentation.Description!));
}
