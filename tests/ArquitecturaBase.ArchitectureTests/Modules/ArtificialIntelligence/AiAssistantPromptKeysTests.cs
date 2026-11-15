using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ArquitecturaBase.ArchitectureTests.Modules.ArtificialIntelligence;

/// <summary>
/// B4: every instruction for the model that the module's code names (a «Prompt.*» key) exists in Spanish and in English, and is
/// read with <c>AssistantTexts.Prompt</c>, which throws if it is missing; <c>Get</c> would send the text of a failure instead.
/// </summary>
public sealed partial class AiAssistantPromptKeysTests
{
    private static readonly string Module = Path.Combine(SolutionRoot.FullPath, "src", "ArquitecturaBase.Application", "Modules", "ArtificialIntelligence");

    [Theory]
    [InlineData("Assistant.resx")]
    [InlineData("Assistant.en.resx")]
    public void Every_instruction_key_the_code_names_exists_in_the_texts(string resource)
    {
        var keys = XDocument.Load(Path.Combine(Module, "Resources", resource)).Root!.Elements("data")
            .Select(data => (string)data.Attribute("name")!).ToHashSet(StringComparer.Ordinal);
        var named = SourceLines().SelectMany(line => PromptKey().Matches(line.Text).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal).ToArray();

        Assert.Contains("Prompt.SystemV2", named);
        Assert.Empty(named.Except(keys, StringComparer.Ordinal));
    }

    [Fact]
    public void An_instruction_is_never_read_with_the_fallback_of_a_failure_code()
    {
        Assert.Matches(PromptReadWithFallback(), "AssistantTexts.Get(\"Prompt.SystemV2\", turn.Culture)");
        Assert.DoesNotMatch(PromptReadWithFallback(), "AssistantTexts.Prompt(\"Prompt.SystemV2\", turn.Culture)");

        Assert.Empty(SourceLines().Where(line => PromptReadWithFallback().IsMatch(line.Text)).Select(line => line.Location));
    }

    private static IEnumerable<(string Location, string Text)> SourceLines() =>
        Directory.EnumerateFiles(Module, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(Module, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .SelectMany(path => File.ReadLines(path).Select((text, index) =>
                ($"{Path.GetRelativePath(SolutionRoot.FullPath, path)}:{index + 1}", text)))
            .Where(line => !line.text.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Select(line => (line.Item1, line.text));

    [GeneratedRegex(@"""(Prompt\.[A-Za-z0-9]+)""")]
    private static partial Regex PromptKey();

    [GeneratedRegex(@"AssistantTexts\.Get\([^;]*""Prompt\.")]
    private static partial Regex PromptReadWithFallback();
}
