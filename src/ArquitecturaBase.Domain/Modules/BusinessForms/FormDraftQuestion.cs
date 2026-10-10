using System.Collections.Frozen;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class FormDraftQuestion
{
    private FormDraftQuestion(Guid definitionId, FormQuestionIdentity identity, IReadOnlyDictionary<string, string> label, IReadOnlyDictionary<string, string>? help,
        bool required, int? maxLength, IEnumerable<FormDraftChoice> choices, IEnumerable<Guid> retiredChoiceIds)
    {
        DefinitionId = definitionId;
        Identity = identity;
        Label = label.ToFrozenDictionary(StringComparer.Ordinal);
        Help = help?.ToFrozenDictionary(StringComparer.Ordinal);
        Required = required;
        MaxLength = maxLength;
        Choices = Array.AsReadOnly(choices.ToArray());
        RetiredChoiceIds = retiredChoiceIds.ToFrozenSet();
    }

    public Guid DefinitionId { get; }

    public FormQuestionIdentity Identity { get; }

    public IReadOnlyDictionary<string, string> Label { get; }

    public IReadOnlyDictionary<string, string>? Help { get; }

    public bool Required { get; }

    public int? MaxLength { get; }

    public IReadOnlyList<FormDraftChoice> Choices { get; }

    internal IReadOnlySet<Guid> RetiredChoiceIds { get; }

    public static Result<FormDraftQuestion> Create(Guid definitionId, FormQuestionType type, IReadOnlyDictionary<string, string> label,
        IReadOnlyDictionary<string, string>? help = null, bool required = false, int? maxLength = null)
    {
        if (definitionId == Guid.Empty)
        {
            return FormDraftErrors.IdentityInvalid;
        }

        if (!HasValidLength(type, maxLength))
        {
            return FormDraftErrors.QuestionConfigurationInvalid;
        }

        var identity = FormQuestionIdentity.Create(type);
        return identity.IsFailure ? identity.Error : new FormDraftQuestion(definitionId, identity.Value, label, help, required, maxLength, [], []);
    }

    public Result<FormDraftQuestion> WithContent(IReadOnlyDictionary<string, string> label, IReadOnlyDictionary<string, string>? help, bool required, int? maxLength)
    {
        if (!HasValidLength(Identity.Type, maxLength))
        {
            return FormDraftErrors.QuestionConfigurationInvalid;
        }

        return new FormDraftQuestion(DefinitionId, Identity, label, help, required, maxLength, Choices, RetiredChoiceIds);
    }

    public Result<FormDraftQuestion> WithChoices(IEnumerable<FormDraftChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var next = choices.ToArray();
        if (next.Length != 0 && Identity.Type != FormQuestionType.Choice)
        {
            return FormDraftErrors.QuestionConfigurationInvalid;
        }

        var ids = next.Select(choice => choice.Identity.ChoiceId).ToHashSet();
        if (ids.Count != next.Length || next.Any(choice => choice.Identity.FieldId != Identity.FieldId))
        {
            return FormDraftErrors.IdentityInvalid;
        }

        if (ids.Overlaps(RetiredChoiceIds))
        {
            return FormDraftErrors.IdentityRetired;
        }

        var retired = RetiredChoiceIds.Concat(Choices.Select(choice => choice.Identity.ChoiceId).Except(ids));
        return new FormDraftQuestion(DefinitionId, Identity, Label, Help, Required, MaxLength, next, retired);
    }

    public Result<FormDraftQuestion> ChangeType(FormQuestionType type)
    {
        if (type == Identity.Type)
        {
            return this;
        }

        return Create(DefinitionId, type, Label, Help, Required);
    }

    public override string ToString() => "FormDraftQuestion { content: [REDACTED] }";

    private static bool HasValidLength(FormQuestionType type, int? maxLength) =>
        maxLength is null || (maxLength > 0 && type is FormQuestionType.ShortText or FormQuestionType.LongText);
}
