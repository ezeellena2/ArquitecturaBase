using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormDraftModelTests
{
    [Fact]
    public void A_stale_revision_is_rejected_without_advancing_the_draft()
    {
        var current = FormDraftRevision.Initial.Advance(1).Value;

        var result = current.Advance(1);

        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.RevisionConflict, result.Error);
        Assert.Equal(2, current.Value);
    }

    [Fact]
    public void A_matching_revision_advances_once_and_keeps_the_previous_value_immutable()
    {
        var initial = FormDraftRevision.Initial;

        var next = initial.Advance(1);

        Assert.True(next.IsSuccess);
        Assert.Equal(2, next.Value.Value);
        Assert.Equal(1, initial.Value);
    }

    [Theory]
    [InlineData(FormQuestionType.ShortText)]
    [InlineData(FormQuestionType.LongText)]
    [InlineData(FormQuestionType.Number)]
    [InlineData(FormQuestionType.Date)]
    [InlineData(FormQuestionType.Boolean)]
    [InlineData(FormQuestionType.Choice)]
    public void Each_manual_type_gets_a_server_generated_identity(FormQuestionType type)
    {
        var result = FormQuestionIdentity.Create(type);

        Assert.True(result.IsSuccess);
        Assert.Equal(type, result.Value.Type);
        Assert.NotEqual(Guid.Empty, result.Value.FieldId);
        Assert.Equal('7', result.Value.FieldId.ToString("D")[14]);
        Assert.NotEqual(result.Value.FieldId, FormQuestionIdentity.Create(type).Value.FieldId);
    }

    [Theory]
    [InlineData(FormQuestionType.Group)]
    [InlineData((FormQuestionType)999)]
    public void Reserved_or_unknown_types_cannot_create_an_active_question(FormQuestionType type)
    {
        var result = FormQuestionIdentity.Create(type);

        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.QuestionTypeUnavailable, result.Error);
    }

    [Fact]
    public void Keeping_the_type_preserves_identity_and_changing_it_creates_another()
    {
        var original = FormQuestionIdentity.Create(FormQuestionType.ShortText).Value;

        Assert.Same(original, original.ChangeType(FormQuestionType.ShortText).Value);
        var changed = original.ChangeType(FormQuestionType.LongText).Value;

        Assert.NotEqual(original.FieldId, changed.FieldId);
        Assert.Equal(FormQuestionType.LongText, changed.Type);
        Assert.Equal(FormQuestionType.ShortText, original.Type);
        Assert.True(original.ChangeType(FormQuestionType.Group).IsFailure);
    }

    [Fact]
    public void A_copy_has_its_own_identity_and_keeps_the_original_type()
    {
        var original = FormQuestionIdentity.Create(FormQuestionType.Choice).Value;

        var copy = original.Copy();

        Assert.NotEqual(original.FieldId, copy.FieldId);
        Assert.Equal(original.Type, copy.Type);
    }
}
