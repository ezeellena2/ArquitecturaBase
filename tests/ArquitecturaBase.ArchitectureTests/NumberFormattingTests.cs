using System.Text.RegularExpressions;

namespace ArquitecturaBase.ArchitectureTests;

// The one number format for people (2026-11-12): a number that reaches a person goes through DisplayNumbers, with the CLDR
// standard of the region of its language (es-AR, en-US). A format of its own here (N0, «#,##0», a culture written by hand or
// a text formatted with a culture) would bypass it. Machine formats keep InvariantCulture and are not checked.
public sealed partial class NumberFormattingTests
{
    private const string Piece = "src/ArquitecturaBase.Application/Common/Formatting/DisplayNumbers.cs";

    [Theory]
    [InlineData("ArquitecturaBase.Api")]
    [InlineData("ArquitecturaBase.Application")]
    [InlineData("ArquitecturaBase.Domain")]
    [InlineData("ArquitecturaBase.Infrastructure")]
    public void Only_display_numbers_writes_a_number_for_a_person(string project)
    {
        var projectDirectory = Path.Combine(SolutionRoot.FullPath, "src", project);

        var loose = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(projectDirectory, path))
            .Where(path => Path.GetRelativePath(SolutionRoot.FullPath, path).Replace('\\', '/') != Piece)
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            .Where(entry => !entry.Line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(entry => DisplayFormat().IsMatch(entry.Line))
            .Select(entry => $"{Path.GetRelativePath(SolutionRoot.FullPath, entry.Path)}:{entry.Number}");

        Assert.Empty(loose);
    }

    // Validation messages leave their numbers to FluentValidation, which writes them in the language of the request: a
    // placeholder of a number carries N0, so «4000» goes as «4.000» and «4,000».
    [Theory]
    [InlineData("Validation.resx")]
    [InlineData("Validation.en.resx")]
    public void A_number_of_a_validation_message_goes_with_its_thousands(string resource)
    {
        var text = File.ReadAllText(Path.Combine(SolutionRoot.FullPath, "src", "ArquitecturaBase.Application", "Resources", resource));

        Assert.Empty(UnformattedNumberPlaceholder().Matches(text).Select(match => match.Value));
    }

    // A percentage goes through DisplayNumbers too (2026-11-12): a sign glued by hand put a space in one place and not in
    // another («21 %», «21%»). A text writes it with {0:percent}; a datum of ARCA, such as the name of a rate, goes as it comes.
    [Fact]
    public void No_text_glues_a_percent_sign_to_a_number()
    {
        var source = Path.Combine(SolutionRoot.FullPath, "src");

        var glued = Directory.EnumerateFiles(source, "*.resx", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(source, path))
            .SelectMany(path => GluedPercentInText().Matches(File.ReadAllText(path))
                .Select(match => $"{Path.GetRelativePath(SolutionRoot.FullPath, path)}: {match.Value}"));

        Assert.Empty(glued);
    }

    // The LIKE patterns of Persistence use «%» as a wildcard, not as a percentage.
    [Theory]
    [InlineData("ArquitecturaBase.Application")]
    [InlineData("ArquitecturaBase.Infrastructure")]
    public void No_code_glues_a_percent_sign_to_a_number(string project)
    {
        var projectDirectory = Path.Combine(SolutionRoot.FullPath, "src", project);

        var glued = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(projectDirectory, path))
            .Where(path => !Path.GetRelativePath(projectDirectory, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("Persistence"))
            .Where(path => Path.GetRelativePath(SolutionRoot.FullPath, path).Replace('\\', '/') != Piece)
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            .Where(entry => !entry.Line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(entry => GluedPercentInCode().IsMatch(entry.Line))
            .Select(entry => $"{Path.GetRelativePath(SolutionRoot.FullPath, entry.Path)}:{entry.Number}");

        Assert.Empty(glued);
    }

    private static bool IsBuildOutput(string projectDirectory, string path) =>
        Path.GetRelativePath(projectDirectory, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    [GeneratedRegex("""\.ToString\(\s*"N\d+"|\.ToString\(\s*"#,|\.ToString\(\s*"[^"]*"\s*,\s*CultureInfo\.GetCultureInfo\(|string\.Format\(\s*CultureInfo\.(?:GetCultureInfo|CurrentCulture|CurrentUICulture)\b|GetCultureInfo\(\s*"(?:es|en)(?:-[A-Z]{2})?"\s*\)""")]
    private static partial Regex DisplayFormat();

    [GeneratedRegex(@"\{(?:MaxLength|MinLength|From|To|ComparisonValue)\}")]
    private static partial Regex UnformattedNumberPlaceholder();

    [GeneratedRegex(@"\{\d+(?::[^}]*)?\}\s?%")]
    private static partial Regex GluedPercentInText();

    [GeneratedRegex("""\+\s*"\s?%"|\{[^{}"]*\}\s?%""")]
    private static partial Regex GluedPercentInCode();
}
