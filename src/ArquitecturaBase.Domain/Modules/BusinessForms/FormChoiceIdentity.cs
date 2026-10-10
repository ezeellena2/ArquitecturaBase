using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class FormChoiceIdentity
{
    private FormChoiceIdentity(Guid fieldId)
    {
        FieldId = fieldId;
        ChoiceId = Guid.CreateVersion7();
    }

    public Guid FieldId { get; }

    public Guid ChoiceId { get; }

    public static Result<FormChoiceIdentity> Create(Guid fieldId)
    {
        if (fieldId == Guid.Empty)
        {
            return FormDraftErrors.IdentityInvalid;
        }

        return new FormChoiceIdentity(fieldId);
    }
}
