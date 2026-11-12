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

    private static bool IsBuildOutput(string projectDirectory, string path) =>
        Path.GetRelativePath(projectDirectory, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    [GeneratedRegex("""\.ToString\(\s*"N\d+"|\.ToString\(\s*"#,|\.ToString\(\s*"[^"]*"\s*,\s*CultureInfo\.GetCultureInfo\(|string\.Format\(\s*CultureInfo\.(?:GetCultureInfo|CurrentCulture|CurrentUICulture)\b|GetCultureInfo\(\s*"(?:es|en)(?:-[A-Z]{2})?"\s*\)""")]
    private static partial Regex DisplayFormat();

    [GeneratedRegex(@"\{(?:MaxLength|MinLength|From|To|ComparisonValue)\}")]
    private static partial Regex UnformattedNumberPlaceholder();
}
