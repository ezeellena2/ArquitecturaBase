using System.Text.RegularExpressions;

namespace ArquitecturaBase.ArchitectureTests;

public sealed partial class MinimalApiRoutesTests
{
    // Las rutas de negocio son controllers MVC. Hoy no hace falta ninguna excepción para los endpoints técnicos, porque
    // ninguno usa estos métodos:
    // - OpenIddict atiende /connect en modo passthrough, a través de ConnectController;
    // - el webhook de WhatsApp es WhatsAppWebhookController;
    // - la salud la mapea MapDefaultEndpoints, que vive en ServiceDefaults (MapHealthChecks) y por eso no se revisa acá;
    // - el documento OpenAPI lo mapea MapOpenApi, y los controllers, MapControllers.
    // Si algún día un endpoint técnico necesita un MapGet, se suma como excepción explícita, con su archivo y su motivo.
    [Theory]
    [InlineData("ArquitecturaBase.Api")]
    [InlineData("ArquitecturaBase.Application")]
    [InlineData("ArquitecturaBase.Infrastructure")]
    public void Project_does_not_map_minimal_api_routes(string project)
    {
        var projectDirectory = Path.Combine(SolutionRoot.FullPath, "src", project);

        var calls = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(projectDirectory, path))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            .Where(entry => !entry.Line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(entry => MinimalApiRoute().IsMatch(entry.Line))
            .Select(entry => $"{Path.GetRelativePath(SolutionRoot.FullPath, entry.Path)}:{entry.Number}");

        Assert.Empty(calls);
    }

    private static bool IsBuildOutput(string projectDirectory, string path) =>
        Path.GetRelativePath(projectDirectory, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    [GeneratedRegex(@"\bMap(?:Get|Post|Put|Delete|Patch|Methods)\b")]
    private static partial Regex MinimalApiRoute();
}
