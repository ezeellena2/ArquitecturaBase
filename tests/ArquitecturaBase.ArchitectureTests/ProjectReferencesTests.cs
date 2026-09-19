using System.Xml.Linq;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class ProjectReferencesTests
{
    public static TheoryData<string, string[]> AllowedReferences => new()
    {
        { "ArquitecturaBase.Domain", [] },
        { "ArquitecturaBase.Application", ["ArquitecturaBase.Domain"] },
        { "ArquitecturaBase.Infrastructure", ["ArquitecturaBase.Application", "ArquitecturaBase.Domain"] },
        { "ArquitecturaBase.Api", ["ArquitecturaBase.Application", "ArquitecturaBase.Infrastructure", "ArquitecturaBase.ServiceDefaults"] },
        { "ArquitecturaBase.AppHost", ["ArquitecturaBase.Api"] },
        { "ArquitecturaBase.ServiceDefaults", [] },
    };

    [Theory]
    [MemberData(nameof(AllowedReferences))]
    public void Project_only_references_allowed_projects(string project, string[] allowed)
    {
        var forbidden = ReadProjectReferences(project).Except(allowed, StringComparer.Ordinal);

        Assert.Empty(forbidden);
    }

    private static string[] ReadProjectReferences(string project)
    {
        var path = Path.Combine(SolutionRoot.FullPath, "src", project, project + ".csproj");

        return XDocument.Load(path)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value.Replace('\\', '/'))
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name!)
            .ToArray();
    }
}
