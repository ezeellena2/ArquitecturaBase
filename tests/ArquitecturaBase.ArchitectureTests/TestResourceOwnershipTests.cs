using System.Xml.Linq;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class TestResourceOwnershipTests
{
    [Fact]
    public void Explicit_test_resources_belong_to_their_test_project()
    {
        var projects = Directory.GetFiles(Path.Combine(SolutionRoot.FullPath, "tests"), "*.csproj", SearchOption.AllDirectories);
        Assert.NotEmpty(projects);
        var violations = projects.SelectMany(project => XDocument.Load(project).Descendants("EmbeddedResource")
            .SelectMany(resource => ((string?)resource.Attribute("Include") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Where(include => !BelongsToProject(Path.GetDirectoryName(project)!, include))
            .Select(include => $"{Path.GetRelativePath(SolutionRoot.FullPath, project)} embeds {include} outside its test project"))
            .ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("Modules/Control/Fixtures/*.json", true)]
    [InlineData("Resources/Example.resx", true)]
    [InlineData("../../docs/reviews/fixtures/*.json", false)]
    [InlineData("../OtherTests/Fixtures/*.json", false)]
    public void The_resource_rule_detects_external_files_and_accepts_owned_fixtures(string include, bool expected)
    {
        var projectDirectory = Path.Combine(SolutionRoot.FullPath, "tests", "ResourceControl");
        Assert.Equal(expected, BelongsToProject(projectDirectory, include));
    }

    private static bool BelongsToProject(string projectDirectory, string include)
    {
        var root = Path.GetFullPath(projectDirectory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(include, projectDirectory);
        return path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
