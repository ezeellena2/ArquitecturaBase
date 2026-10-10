using System.Collections.Frozen;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class FormDraftSnapshot
{
    private FormDraftSnapshot(Guid definitionId, FormDraftRevision revision, IEnumerable<FormDraftQuestion> questions,
        IEnumerable<Guid> retiredFieldIds, IEnumerable<Guid> retiredChoiceIds)
    {
        DefinitionId = definitionId;
        Revision = revision;
        Questions = Array.AsReadOnly(questions.ToArray());
        RetiredFieldIds = retiredFieldIds.ToFrozenSet();
        RetiredChoiceIds = retiredChoiceIds.ToFrozenSet();
    }

    public Guid DefinitionId { get; }

    public FormDraftRevision Revision { get; }

    public IReadOnlyList<FormDraftQuestion> Questions { get; }

    public IReadOnlySet<Guid> RetiredFieldIds { get; }

    public IReadOnlySet<Guid> RetiredChoiceIds { get; }

    public static Result<FormDraftSnapshot> Create(Guid definitionId, IEnumerable<FormDraftQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var next = questions.ToArray();
        var valid = ValidateOwnership(definitionId, next);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        return new FormDraftSnapshot(definitionId, FormDraftRevision.Initial, next, [], next.SelectMany(question => question.RetiredChoiceIds));
    }

    public Result<FormDraftSnapshot> Update(long expectedRevision, IEnumerable<FormDraftQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var revision = Revision.Advance(expectedRevision);
        if (revision.IsFailure)
        {
            return revision.Error;
        }

        var next = questions.ToArray();
        var valid = ValidateOwnership(DefinitionId, next);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var fieldIds = next.Select(question => question.Identity.FieldId).ToHashSet();
        var choiceIds = next.SelectMany(question => question.Choices).Select(choice => choice.Identity.ChoiceId).ToHashSet();
        if (fieldIds.Overlaps(RetiredFieldIds) || choiceIds.Overlaps(RetiredChoiceIds))
        {
            return FormDraftErrors.IdentityRetired;
        }

        var retiredFields = RetiredFieldIds.Concat(Questions.Select(question => question.Identity.FieldId).Except(fieldIds));
        var retiredChoices = RetiredChoiceIds
            .Concat(Questions.SelectMany(question => question.Choices).Select(choice => choice.Identity.ChoiceId).Except(choiceIds))
            .Concat(next.SelectMany(question => question.RetiredChoiceIds));
        return new FormDraftSnapshot(DefinitionId, revision.Value, next, retiredFields, retiredChoices);
    }

    public override string ToString() => "FormDraftSnapshot { content: [REDACTED] }";

    private static Result ValidateOwnership(Guid definitionId, FormDraftQuestion[] questions)
    {
        if (definitionId == Guid.Empty || questions.Any(question => question.DefinitionId != definitionId)
            || questions.Select(question => question.Identity.FieldId).Distinct().Count() != questions.Length)
        {
            return FormDraftErrors.IdentityInvalid;
        }

        return Result.Success();
    }
}
