using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class FormDraftRevision
{
    private FormDraftRevision(long value) => Value = value;

    public static FormDraftRevision Initial { get; } = new(1);

    public long Value { get; }

    public Result<FormDraftRevision> Advance(long expectedRevision)
    {
        if (expectedRevision != Value)
        {
            return FormDraftErrors.RevisionConflict;
        }

        return new FormDraftRevision(Value + 1);
    }
}
