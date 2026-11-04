using System.Text.RegularExpressions;

namespace ArquitecturaBase.ArchitectureTests.Modules.Arca;

/// <summary>
/// Las familias de servicios del módulo ARCA (ADR 0025): una familia usa sólo Connection, y Connection no nombra a ninguna.
/// La familia de un archivo sale de su ruta (la primera carpeta que es una familia) y la de un nombre, de su namespace. La
/// raíz del módulo (registros, permisos, errores, opciones) compone a todas. Lee el fuente, como ModuleBoundaryTests: un
/// using o un nombre completo de otra familia rompen el build al quitarla.
/// </summary>
public sealed partial class ArcaFamilyBoundaryTests
{
    private const string Connection = "Connection";

    // Una familia nueva se suma acá; Every_family_folder_is_a_declared_family avisa si una carpeta no está.
    private static readonly string[] Families = [Connection, "Registry", "Invoicing"];

    private static readonly string[] Projects = ["Domain", "Application", "Infrastructure", "Api"];

    // Las carpetas cuyas subcarpetas son familias. Domain e Infrastructure las tienen en la raíz del módulo.
    private static readonly string[] FamilyParents =
    [
        "src/ArquitecturaBase.Domain/Modules/Arca",
        "src/ArquitecturaBase.Infrastructure/Modules/Arca",
        "src/ArquitecturaBase.Infrastructure/Modules/Arca/Persistence/Configurations",
        "src/ArquitecturaBase.Infrastructure/Modules/Arca/Persistence/Repositories",
        "src/ArquitecturaBase.Infrastructure/Modules/Arca/Persistence/Readers",
        "src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Services",
        "src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Persistence",
        "src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Integrations",
        "src/ArquitecturaBase.Application/Modules/Arca/Interfaces/Channels",
        "src/ArquitecturaBase.Application/Modules/Arca/Channels",
        "src/ArquitecturaBase.Application/Modules/Arca/Models",
        "src/ArquitecturaBase.Application/Modules/Arca/Services",
        "src/ArquitecturaBase.Api/Modules/Arca/Contracts",
    ];

    // Carpetas de esas raíces que no son una familia.
    private static readonly string[] NotFamilies = ["Persistence"];

    [Fact]
    public void A_family_names_only_its_own_types_and_the_connection()
    {
        var violations = SourceFiles().SelectMany(file => Violations(file.Key, file.Value)).ToArray();

        Assert.True(
            violations.Length == 0,
            "An ARCA family names another:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void The_rule_reports_a_family_that_names_another_and_the_connection_that_names_a_family()
    {
        const string Invoicing = "src/ArquitecturaBase.Application/Modules/Arca/Services/Invoicing/X.cs";
        const string ConnectionFile = "src/ArquitecturaBase.Infrastructure/Modules/Arca/Connection/Soap/X.cs";
        const string Root = "src/ArquitecturaBase.Infrastructure/Modules/Arca/ArcaInfrastructureRegistration.cs";
        const string Configuration = "src/ArquitecturaBase.Infrastructure/Modules/Arca/Persistence/Configurations/Invoicing/X.cs";

        // Una familia usa lo suyo, la conexión y la raíz del módulo.
        Assert.Empty(Violations(Invoicing, "using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;"));
        Assert.Empty(Violations(Invoicing, "using ArquitecturaBase.Domain.Modules.Arca.Invoicing;"));
        Assert.Empty(Violations(Invoicing, "using ArquitecturaBase.Domain.Modules.Arca;"));

        // No usa otra familia, ni con un using ni con un nombre completo.
        Assert.NotEmpty(Violations(Invoicing, "using ArquitecturaBase.Application.Modules.Arca.Services.Registry;"));
        Assert.NotEmpty(Violations(Invoicing, "var t = typeof(global::ArquitecturaBase.Domain.Modules.Arca.Registry.ArcaRegistryCompany);"));

        // La conexión no nombra a ninguna familia.
        Assert.NotEmpty(Violations(ConnectionFile, "using ArquitecturaBase.Domain.Modules.Arca.Invoicing;"));
        Assert.Empty(Violations(ConnectionFile, "using ArquitecturaBase.Domain.Modules.Arca.Connection;"));

        // La raíz compone a todas.
        Assert.Empty(Violations(Root, """
            using ArquitecturaBase.Infrastructure.Modules.Arca.Registry;
            using ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;
            """));

        // La familia de un archivo es la primera carpeta que es una familia, también debajo de las de capa.
        Assert.Empty(Violations(Configuration, "using ArquitecturaBase.Domain.Modules.Arca.Invoicing;"));
        Assert.NotEmpty(Violations(Configuration, "using ArquitecturaBase.Domain.Modules.Arca.Registry;"));

        // Un tipo cuyo nombre contiene el de una familia no es esa familia.
        Assert.Empty(Violations(Invoicing, "using ArquitecturaBase.Application.Modules.Arca.Models.ArcaRegistryLike;"));
    }

    [Fact]
    public void Every_family_folder_is_a_declared_family()
    {
        var undeclared = FamilyFolders().Where(folder => !Families.Contains(folder, StringComparer.Ordinal)).ToArray();

        Assert.True(
            undeclared.Length == 0,
            "Declare these ARCA families in ArcaFamilyBoundaryTests.Families: " + string.Join(", ", undeclared));
    }

    /// <summary>
    /// Lo que <paramref name="relativePath"/> nombra de una familia que no puede usar: otra familia si el archivo es de una
    /// familia, cualquier familia si es de Connection, y nada si está en la raíz del módulo.
    /// </summary>
    private static IEnumerable<string> Violations(string relativePath, string text)
    {
        if (FamilyOfPath(relativePath) is not { } own)
        {
            return [];
        }

        return FamilyNames(text)
            .Where(family => family != own && (own == Connection || family != Connection))
            .Select(family => $"{relativePath} ({own}) names the family {family}");
    }

    private static string? FamilyOfPath(string relativePath)
    {
        const string Marker = "/Modules/Arca/";
        var start = relativePath.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var folders = relativePath[(start + Marker.Length)..].Split('/')[..^1];
        return folders.FirstOrDefault(folder => Families.Contains(folder, StringComparer.Ordinal));
    }

    private static IEnumerable<string> FamilyNames(string text) =>
        ArcaName().Matches(text)
            .Select(match => match.Groups["rest"].Value.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(segment => Families.Contains(segment, StringComparer.Ordinal)))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);

    private static IEnumerable<string> FamilyFolders() =>
        FamilyParents
            .Select(parent => Path.Combine(SolutionRoot.FullPath, parent))
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(folder => !NotFamilies.Contains(folder, StringComparer.Ordinal) && folder is not ("bin" or "obj"))
            .Distinct(StringComparer.Ordinal);

    /// <summary>Los .cs de las carpetas Modules/Arca de los cuatro proyectos, por su ruta desde la raíz.</summary>
    private static Dictionary<string, string> SourceFiles() =>
        Projects
            .Select(project => Path.Combine(SolutionRoot.FullPath, "src", $"ArquitecturaBase.{project}", "Modules", "Arca"))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .ToDictionary(
                path => Path.GetRelativePath(SolutionRoot.FullPath, path).Replace('\\', '/'),
                File.ReadAllText,
                StringComparer.Ordinal);

    // El nombre completo de algo del módulo ARCA en cualquier capa; "rest" es lo que sigue a Modules.Arca.
    [GeneratedRegex(@"\bArquitecturaBase\.(Domain|Application|Infrastructure|Api)\.Modules\.Arca(?<rest>(\.[A-Za-z_][A-Za-z0-9_]*)*)")]
    private static partial Regex ArcaName();
}
