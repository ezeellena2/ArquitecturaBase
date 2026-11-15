using System.Globalization;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

/// <summary>
/// B4: an instruction for the model that is missing from the texts throws, so a renamed key never reaches the model as the text
/// of a failure. A failure code keeps its fallback.
/// </summary>
public sealed class AssistantTextsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void A_missing_instruction_throws_instead_of_reaching_the_model_as_a_failure_text(string culture) =>
        Assert.Throws<InvalidOperationException>(() => AssistantTexts.Prompt("Prompt.Missing", culture));

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void An_instruction_is_read_in_the_turn_language(string culture) => Assert.Equal(
        AssistantTexts.Manager.GetString("Prompt.SystemV2", CultureInfo.GetCultureInfo(culture)), AssistantTexts.Prompt("Prompt.SystemV2", culture));

    [Fact]
    public void A_failure_code_without_its_text_keeps_the_provider_unavailable_text() =>
        Assert.Equal(AssistantTexts.Get("Ai.Assistant.ProviderUnavailable"), AssistantTexts.Get("Ai.Assistant.Missing", "es"));
}
