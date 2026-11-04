using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// Turns what the assistant proposes into the same requests the authoring uses, so both obey one set of rules.
internal static class FormAuthoringProposalPolicy
{
    public static readonly string[] QuestionTypes = ["shortText", "longText", "number", "date", "yesNo", "choice", "multiChoice", "catalog", "photo", "document"];
    public static readonly string[] FieldTypes = ["text", "integer", "decimal", "date", "boolean"];
    public static readonly string[] ValueTypes = ["text", "integer", "decimal", "date"];

    public static FormAuthoringFormProposal Normalize(FormAuthoringFormProposal proposal) => proposal with
    {
        Name = proposal.Name.Trim(), Prefix = proposal.Prefix.Trim().ToUpperInvariant(),
        SubmitLabel = string.IsNullOrWhiteSpace(proposal.SubmitLabel) ? proposal.Culture == "en" ? "Submit" : "Enviar" : proposal.SubmitLabel.Trim(),
        Questions = proposal.Questions.Select(question => question with
        {
            Label = question.Label.Trim(), Options = question.Options.Select(option => option.Trim()).ToArray(),
        }).ToArray(),
    };

    // A question's kind decides its options, catalog, files and selection; anything else is a malformed proposal.
    public static bool HasValidShape(FormAuthoringFormProposal proposal) => proposal.Culture is "es" or "en" &&
        proposal.Questions.Count is >= 1 and <= 40 && proposal.Questions.All(question => QuestionTypes.Contains(question.Type, StringComparer.Ordinal) &&
            (question.Type is "choice" or "multiChoice" ? question.Options.Count > 0 : question.Options.Count == 0) &&
            (question.Type == "catalog" ? question.Catalog is { Id: var id } && id != Guid.Empty : question.Catalog is null));

    public static SaveFormDraftRequest Draft(Guid requestId, FormAuthoringFormProposal proposal) => new(requestId,
        new Dictionary<string, string> { [proposal.Culture] = proposal.Name }, proposal.Prefix,
        new FormDraftInput(1, proposal.Culture, new Dictionary<string, string> { [proposal.Culture] = proposal.SubmitLabel },
            proposal.Questions.Select((question, index) => Field("q" + (index + 1), question, proposal.Culture)).ToArray()));

    private static FormFieldInput Field(string localId, FormAuthoringProposedQuestion question, string culture)
    {
        var label = new Dictionary<string, string> { [culture] = question.Label };
        var minimum = question.Required ? 1 : 0;
        return question.Type switch
        {
            "number" => new(localId, null, null, "decimal", label, null, question.Required, null, null),
            "yesNo" => new(localId, null, null, "boolean", label, null, question.Required, null, null),
            "choice" => new(localId, null, null, "choice", label, null, question.Required, null, Options(localId, question.Options, culture)),
            "multiChoice" => new(localId, null, null, "multiChoice", label, null, question.Required, null, Options(localId, question.Options, culture),
                Selection: new(minimum, question.Options.Count)),
            "catalog" => new(localId, null, null, "choice", label, null, question.Required, null, null, Source: new(question.Catalog!.Id)),
            "photo" or "document" => new(localId, null, null, question.Type, label, null, question.Required, null, null,
                Files: new(minimum, 1, question.Type == "photo" ? "cameraOrGallery" : null)),
            _ => new(localId, null, null, question.Type, label, null, question.Required, null, null),
        };
    }

    private static FormChoiceInput[] Options(string localId, IReadOnlyList<string> options, string culture) =>
        options.Select((option, index) => new FormChoiceInput(localId + "o" + (index + 1), null,
            new Dictionary<string, string> { [culture] = option })).ToArray();

    public static Result<FormAuthoringCatalogProposal> Validate(FormAuthoringCatalogProposal proposal)
    {
        var normalized = proposal with
        {
            Name = proposal.Name.Trim(), Description = proposal.Description.Trim(), Code = proposal.Code.Trim(), Option = proposal.Option.Trim(),
            Fields = proposal.Fields.Select(field => field with { Name = field.Name.Trim() }).ToArray(),
        };
        var valid = normalized.Culture is "es" or "en" && normalized.Name.Length is > 0 and <= 120 && normalized.Description.Length <= 1000 &&
            ValueTypes.Contains(normalized.ValueType, StringComparer.Ordinal) && normalized.Code.Length is > 0 and <= 80 &&
            normalized.Option.Length is > 0 and <= 80 && normalized.Fields.Count <= BusinessFormCatalog.MaxFields &&
            normalized.Fields.All(field => field.Name.Length is > 0 and <= 120 && FieldTypes.Contains(field.Type, StringComparer.Ordinal)) &&
            normalized.Fields.Select(field => field.Name.ToUpperInvariant()).Distinct(StringComparer.Ordinal).Count() == normalized.Fields.Count;
        return valid ? normalized : FormCatalogErrors.ContentInvalid;
    }
}
