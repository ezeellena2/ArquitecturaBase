using System.Text;
using System.Text.RegularExpressions;

namespace ArquitecturaBase.ArchitectureTests.Support;

/// <summary>
/// La cadena de guías de una carpeta: el AGENTS.md (Codex) y el CLAUDE.md (Claude Code) de cada carpeta desde la raíz
/// hasta ella, más lo que esos archivos importan con <c>@ruta</c>, cada archivo una vez. Como en Claude Code, un import
/// dentro de código (entre comillas invertidas o en un bloque) no se sigue, ni uno a un archivo que no existe, y la
/// cadena de imports llega hasta cinco saltos.
/// </summary>
internal static partial class InstructionChains
{
    private const int MaxImportDepth = 5;
    private static readonly string[] Guides = ["AGENTS.md", "CLAUDE.md"];

    // Lo que genera la compilación o una herramienta; `.github` y las demás carpetas ocultas con guía sí cuentan.
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".artifacts", ".test-artifacts", ".vs", ".idea", "bin", "obj", "node_modules", "TestResults",
    };

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Cada carpeta con un AGENTS.md debajo de <paramref name="root"/>, incluida la raíz.</summary>
    public static IReadOnlyList<string> Folders(string root)
    {
        var folders = new List<string>();
        var pending = new Stack<string>([Path.GetFullPath(root)]);

        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            if (File.Exists(Path.Combine(folder, "AGENTS.md")))
            {
                folders.Add(folder);
            }

            foreach (var child in Directory.EnumerateDirectories(folder).Where(child => !SkippedFolders.Contains(Path.GetFileName(child))))
            {
                pending.Push(child);
            }
        }

        return folders;
    }

    /// <summary>Los archivos que lee un agente que trabaja en <paramref name="folder"/>, sin repetir.</summary>
    public static IReadOnlyCollection<string> Files(string root, string folder)
    {
        var chain = new HashSet<string>(PathComparer);
        var directories = new List<string> { Path.GetFullPath(root) };
        var relative = Path.GetRelativePath(directories[0], Path.GetFullPath(folder));
        if (relative != ".")
        {
            foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                directories.Add(Path.Combine(directories[^1], segment));
            }
        }

        foreach (var guide in directories.SelectMany(directory => Guides.Select(name => Path.Combine(directory, name))))
        {
            Add(guide, depth: 0);
        }

        return chain;

        void Add(string file, int depth)
        {
            var path = Path.GetFullPath(file);
            if (depth > MaxImportDepth || !File.Exists(path) || !chain.Add(path))
            {
                return;
            }

            foreach (var import in Imports(File.ReadAllText(path)))
            {
                Add(Path.Combine(Path.GetDirectoryName(path)!, import), depth + 1);
            }
        }
    }

    /// <summary>El tamaño de la cadena en bytes UTF-8 con fin de línea LF, como la guarda Git en cualquier sistema.</summary>
    public static long Bytes(string root, string folder) =>
        Files(root, folder).Sum(file => (long)Encoding.UTF8.GetByteCount(File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal)));

    /// <summary>Las cadenas que pasan de <paramref name="limit"/> bytes, de la más larga a la más corta.</summary>
    public static IReadOnlyList<(string Folder, long Bytes)> Oversized(string root, long limit) =>
        [.. Folders(root)
            .Select(folder => (Folder: Path.GetRelativePath(Path.GetFullPath(root), folder).Replace('\\', '/'), Bytes: Bytes(root, folder)))
            .Where(chain => chain.Bytes > limit)
            .OrderByDescending(chain => chain.Bytes)];

    private static IEnumerable<string> Imports(string content)
    {
        var prose = InlineCode().Replace(CodeBlock().Replace(content, string.Empty), string.Empty);
        return Import().Matches(prose).Select(match => match.Groups["path"].Value);
    }

    [GeneratedRegex("```[\\s\\S]*?```")]
    private static partial Regex CodeBlock();

    [GeneratedRegex("`[^`\\n]*`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"(?<![^\s])@(?<path>[^\s]+)")]
    private static partial Regex Import();
}
