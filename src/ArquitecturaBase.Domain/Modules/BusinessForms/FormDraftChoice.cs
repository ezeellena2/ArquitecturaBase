using System.Collections.Frozen;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class FormDraftChoice
{
    private FormDraftChoice(FormChoiceIdentity identity, IReadOnlyDictionary<string, string> label)
    {
        Identity = identity;
        Label = label.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public FormChoiceIdentity Identity { get; }

    public IReadOnlyDictionary<string, string> Label { get; }

    public static Result<FormDraftChoice> Create(Guid fieldId, IReadOnlyDictionary<string, string> label)
    {
        if (label is null || !label.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            return FormDraftErrors.ChoiceLabelRequired;
        }

        var identity = FormChoiceIdentity.Create(fieldId);
        return identity.IsFailure ? identity.Error : new FormDraftChoice(identity.Value, label);
    }

    public Result<FormDraftChoice> Rename(IReadOnlyDictionary<string, string> label)
    {
        if (label is null || !label.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
        {
            return FormDraftErrors.ChoiceLabelRequired;
        }

        return new FormDraftChoice(Identity, label);
    }

    public override string ToString() => "FormDraftChoice { content: [REDACTED] }";
}
