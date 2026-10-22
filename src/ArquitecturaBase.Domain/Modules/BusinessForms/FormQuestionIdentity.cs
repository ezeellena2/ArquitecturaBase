using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class FormQuestionIdentity
{
    private FormQuestionIdentity(FormQuestionType type)
    {
        FieldId = Guid.CreateVersion7();
        Type = type;
    }

    public Guid FieldId { get; }

    public FormQuestionType Type { get; }

    public static Result<FormQuestionIdentity> Create(FormQuestionType type)
    {
        if (type is not (FormQuestionType.ShortText or FormQuestionType.LongText or FormQuestionType.Number
            or FormQuestionType.Date or FormQuestionType.Boolean or FormQuestionType.Choice or FormQuestionType.MultiChoice or FormQuestionType.Photo or FormQuestionType.Document))
        {
            return FormDraftErrors.QuestionTypeUnavailable;
        }

        return new FormQuestionIdentity(type);
    }

    public Result<FormQuestionIdentity> ChangeType(FormQuestionType type)
    {
        if (type == Type)
        {
            return this;
        }

        return Create(type);
    }

    public FormQuestionIdentity Copy() => new(Type);
}
