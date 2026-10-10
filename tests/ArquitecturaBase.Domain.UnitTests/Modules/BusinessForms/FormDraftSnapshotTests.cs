using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormDraftSnapshotTests
{
    private static Dictionary<string, string> Text(string value) => new() { ["es"] = value };

    [Fact]
    public void A_removed_option_cannot_be_readded_to_the_question()
    {
        var question = FormDraftQuestion.Create(Guid.CreateVersion7(), FormQuestionType.Choice, Text("Shift")).Value;
        var choice = FormDraftChoice.Create(question.Identity.FieldId, Text("Morning")).Value;
        var removed = question.WithChoices([choice]).Value.WithChoices([]).Value;

        Assert.Equal(FormDraftErrors.IdentityRetired, removed.WithChoices([choice]).Error);
    }

    [Fact]
    public void A_choice_is_owned_by_its_question_and_renaming_keeps_its_identity()
    {
        var fieldId = Guid.CreateVersion7();
        var choice = FormDraftChoice.Create(fieldId, Text("Morning")).Value;

        var renamed = choice.Rename(Text("First shift")).Value;

        Assert.Equal(fieldId, choice.Identity.FieldId);
        Assert.Equal(choice.Identity.ChoiceId, renamed.Identity.ChoiceId);
        Assert.Equal('7', choice.Identity.ChoiceId.ToString("D")[14]);
        Assert.Equal("Morning", choice.Label!["es"]);
        Assert.Equal("First shift", renamed.Label["es"]);
    }

    [Fact]
    public void An_empty_parent_cannot_own_a_choice()
    {
        var result = FormChoiceIdentity.Create(Guid.Empty);

        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.IdentityInvalid, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_choice_cannot_have_a_blank_label(string label)
    {
        var result = FormDraftChoice.Create(Guid.CreateVersion7(), Text(label));

        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.ChoiceLabelRequired, result.Error);
    }

    [Theory]
    [InlineData(FormQuestionType.ShortText)]
    [InlineData(FormQuestionType.LongText)]
    [InlineData(FormQuestionType.Number)]
    [InlineData(FormQuestionType.Date)]
    [InlineData(FormQuestionType.Boolean)]
    [InlineData(FormQuestionType.Choice)]
    public void The_six_manual_types_can_form_an_owned_draft(FormQuestionType type)
    {
        var owner = Guid.CreateVersion7();
        var question = FormDraftQuestion.Create(owner, type, Text("Question"), Text("Employee help"), true).Value;

        var draft = FormDraftSnapshot.Create(owner, [question]).Value;

        Assert.Equal(owner, question.DefinitionId);
        Assert.Equal(type, draft.Questions[0].Identity.Type);
        Assert.Equal("Employee help", draft.Questions[0].Help!["es"]);
        Assert.True(draft.Questions[0].Required);
        Assert.Equal(1, draft.Revision.Value);
    }

    [Theory]
    [InlineData(FormQuestionType.Number)]
    [InlineData(FormQuestionType.Date)]
    [InlineData(FormQuestionType.Boolean)]
    [InlineData(FormQuestionType.Choice)]
    public void Only_text_questions_accept_a_text_length(FormQuestionType type)
    {
        var result = FormDraftQuestion.Create(Guid.CreateVersion7(), type, Text("Question"), maxLength: 80);

        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.QuestionConfigurationInvalid, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_text_length_must_be_positive(int maxLength)
    {
        var result = FormDraftQuestion.Create(Guid.CreateVersion7(), FormQuestionType.ShortText, Text("Question"), maxLength: maxLength);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void A_choice_from_another_question_cannot_be_moved_here()
    {
        var question = FormDraftQuestion.Create(Guid.CreateVersion7(), FormQuestionType.Choice, Text("Shift")).Value;
        var foreign = FormDraftChoice.Create(Guid.CreateVersion7(), Text("Morning")).Value;

        var result = question.WithChoices([foreign]);

        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.IdentityInvalid, result.Error);
        Assert.Empty(question.Choices);
    }

    [Fact]
    public void A_non_choice_question_cannot_contain_options()
    {
        var question = FormDraftQuestion.Create(Guid.CreateVersion7(), FormQuestionType.Boolean, Text("Pickup")).Value;
        var choice = FormDraftChoice.Create(question.Identity.FieldId, Text("Yes")).Value;

        var result = question.WithChoices([choice]);

        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.QuestionConfigurationInvalid, result.Error);
    }

    [Fact]
    public void Duplicated_choice_identity_is_rejected()
    {
        var question = FormDraftQuestion.Create(Guid.CreateVersion7(), FormQuestionType.Choice, Text("Shift")).Value;
        var choice = FormDraftChoice.Create(question.Identity.FieldId, Text("Morning")).Value;

        Assert.True(question.WithChoices([choice, choice]).IsFailure);
    }

    [Fact]
    public void Renaming_and_reordering_options_preserves_identity_and_old_content()
    {
        var question = FormDraftQuestion.Create(Guid.CreateVersion7(), FormQuestionType.Choice, Text("Shift")).Value;
        var morning = FormDraftChoice.Create(question.Identity.FieldId, Text("Morning")).Value;
        var evening = FormDraftChoice.Create(question.Identity.FieldId, Text("Evening")).Value;
        var original = question.WithChoices([morning, evening]).Value;

        var next = original.WithChoices([evening, morning.Rename(Text("First shift")).Value]).Value;

        Assert.Equal(question.Identity.FieldId, next.Identity.FieldId);
        Assert.Equal(evening.Identity.ChoiceId, next.Choices[0].Identity.ChoiceId);
        Assert.Equal(morning.Identity.ChoiceId, next.Choices[1].Identity.ChoiceId);
        Assert.Equal("Morning", original.Choices[0].Label!["es"]);
    }

    [Fact]
    public void A_removed_option_cannot_be_restored_from_an_old_question_object()
    {
        var owner = Guid.CreateVersion7();
        var question = FormDraftQuestion.Create(owner, FormQuestionType.Choice, Text("Shift")).Value;
        var choice = FormDraftChoice.Create(question.Identity.FieldId, Text("Morning")).Value;
        var withOption = question.WithChoices([choice]).Value;
        var original = FormDraftSnapshot.Create(owner, [withOption]).Value;
        var removed = original.Update(1, [withOption.WithChoices([]).Value]).Value;

        var result = removed.Update(2, [withOption]);

        Assert.True(result.IsFailure);
        Assert.Equal(FormDraftErrors.IdentityRetired, result.Error);
        Assert.Empty(removed.Questions[0].Choices);
    }

    [Fact]
    public void A_removed_question_cannot_be_restored_and_a_readded_question_gets_a_new_identity()
    {
        var owner = Guid.CreateVersion7();
        var question = FormDraftQuestion.Create(owner, FormQuestionType.ShortText, Text("Reference")).Value;
        var original = FormDraftSnapshot.Create(owner, [question]).Value;
        var removed = original.Update(1, []).Value;

        Assert.Equal(FormDraftErrors.IdentityRetired, removed.Update(2, [question]).Error);
        var readded = FormDraftQuestion.Create(owner, FormQuestionType.ShortText, Text("Reference")).Value;
        Assert.NotEqual(question.Identity.FieldId, readded.Identity.FieldId);
        Assert.True(removed.Update(2, [readded]).IsSuccess);
    }

    [Fact]
    public void Changing_type_back_creates_another_identity_and_retires_the_original_options()
    {
        var owner = Guid.CreateVersion7();
        var question = FormDraftQuestion.Create(owner, FormQuestionType.Choice, Text("Shift")).Value;
        var choice = FormDraftChoice.Create(question.Identity.FieldId, Text("Morning")).Value;
        question = question.WithChoices([choice]).Value;
        var original = FormDraftSnapshot.Create(owner, [question]).Value;
        var text = question.ChangeType(FormQuestionType.ShortText).Value;
        var changed = original.Update(1, [text]).Value;

        var back = text.ChangeType(FormQuestionType.Choice).Value;

        Assert.NotEqual(question.Identity.FieldId, back.Identity.FieldId);
        Assert.NotEqual(text.Identity.FieldId, back.Identity.FieldId);
        Assert.Empty(back.Choices);
        Assert.Contains(choice.Identity.ChoiceId, changed.RetiredChoiceIds);
        Assert.Equal(FormDraftErrors.IdentityRetired, changed.Update(2, [question]).Error);
    }

    [Fact]
    public void A_question_owned_by_another_definition_is_rejected()
    {
        var owner = Guid.CreateVersion7();
        var draft = FormDraftSnapshot.Create(owner, []).Value;
        var foreign = FormDraftQuestion.Create(Guid.CreateVersion7(), FormQuestionType.Date, Text("Date")).Value;

        var result = draft.Update(1, [foreign]);

        Assert.Equal(FormDraftErrors.IdentityInvalid, result.Error);
        Assert.Empty(draft.Questions);
        Assert.Equal(1, draft.Revision.Value);
    }

    [Fact]
    public void Duplicate_question_identity_is_rejected()
    {
        var owner = Guid.CreateVersion7();
        var question = FormDraftQuestion.Create(owner, FormQuestionType.Date, Text("Date")).Value;

        Assert.True(FormDraftSnapshot.Create(owner, [question, question]).IsFailure);
    }

    [Fact]
    public void Draft_and_option_collections_do_not_change_when_the_input_list_changes()
    {
        var owner = Guid.CreateVersion7();
        var question = FormDraftQuestion.Create(owner, FormQuestionType.Choice, Text("Shift")).Value;
        var options = new List<FormDraftChoice> { FormDraftChoice.Create(question.Identity.FieldId, Text("Morning")).Value };
        question = question.WithChoices(options).Value;
        var questions = new List<FormDraftQuestion> { question };
        var draft = FormDraftSnapshot.Create(owner, questions).Value;

        options.Clear();
        questions.Clear();

        Assert.Single(draft.Questions);
        Assert.Single(draft.Questions[0].Choices);
        Assert.Throws<NotSupportedException>(() => ((IList<FormDraftQuestion>)draft.Questions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<FormDraftChoice>)question.Choices).Clear());
    }

    [Fact]
    public void A_batch_edit_advances_once_and_a_stale_revision_changes_nothing()
    {
        var owner = Guid.CreateVersion7();
        var first = FormDraftQuestion.Create(owner, FormQuestionType.Boolean, Text("Pickup")).Value;
        var second = FormDraftQuestion.Create(owner, FormQuestionType.Date, Text("Date")).Value;
        var draft = FormDraftSnapshot.Create(owner, [first, second]).Value;

        var next = draft.Update(1, [second, first.WithContent(Text("Collect"), Text("Help"), true, null).Value]).Value;

        Assert.Equal(2, next.Revision.Value);
        Assert.Equal(FormDraftErrors.RevisionConflict, next.Update(1, []).Error);
        Assert.Equal("Pickup", draft.Questions[0].Label!["es"]);
        Assert.Equal(first.Identity.FieldId, next.Questions[1].Identity.FieldId);
    }
}
