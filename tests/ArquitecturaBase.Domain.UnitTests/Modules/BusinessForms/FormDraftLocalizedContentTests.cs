using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;
public sealed class FormDraftLocalizedContentTests
{
    [Fact]
    public void Localized_question_and_choice_content_is_copied_without_changing_identity()
    {
        var labels = new Dictionary<string, string> { ["es"] = "Pregunta", ["en"] = "" };
        var help = new Dictionary<string, string> { ["es"] = "Ayuda" };
        var question = FormDraftQuestion.Create(Guid.CreateVersion7(), FormQuestionType.Choice, labels, help).Value;
        var choice = FormDraftChoice.Create(question.Identity.FieldId, labels).Value;
        labels["es"] = "Changed outside"; help["es"] = "Changed outside";
        Assert.Equal("Pregunta", question.Label["es"]);
        Assert.Equal("Ayuda", question.Help!["es"]);
        Assert.Equal("Pregunta", choice.Label["es"]);
        var translated = choice.Rename(new Dictionary<string, string> { ["es"] = "Pregunta", ["en"] = "Question" }).Value;
        Assert.Equal(choice.Identity.ChoiceId, translated.Identity.ChoiceId);
        Assert.Equal("Question", translated.Label["en"]);
        Assert.Equal("", choice.Label["en"]);
    }
}