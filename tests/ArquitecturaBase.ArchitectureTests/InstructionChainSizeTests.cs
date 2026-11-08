using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// Lo que lee un agente al trabajar en una carpeta: el AGENTS.md y el CLAUDE.md de cada carpeta desde la raíz hasta ella,
/// más lo que importan con <c>@ruta</c>. Codex corta sin avisar lo que pasa de 32 KiB, así que cada cadena queda por debajo
/// de 30 KB (<c>docs/guides/instrucciones-por-carpeta.md</c>, «Tamaño de las guías»).
/// </summary>
public sealed class InstructionChainSizeTests
{
    private const long MaxChainBytes = 30_000;

    [Fact]
    public void Every_folder_with_instructions_keeps_what_an_agent_reads_under_30_kb()
    {
        Assert.NotEmpty(InstructionChains.Folders(SolutionRoot.FullPath));

        var oversized = InstructionChains.Oversized(SolutionRoot.FullPath, MaxChainBytes);

        Assert.True(oversized.Count == 0, "Shorten these instruction chains (docs/guides/instrucciones-por-carpeta.md):"
            + Environment.NewLine + string.Join(Environment.NewLine, oversized.Select(chain => $"{chain.Folder}: {chain.Bytes} bytes")));
    }

    [Fact]
    public void A_chain_adds_the_guides_from_the_root_and_each_import_once()
    {
        using var tree = new GuideTree();
        tree.Write("AGENTS.md", "root rules");
        tree.Write("CLAUDE.md", "@AGENTS.md\nclaude");
        tree.Write("area/AGENTS.md", "area rules");
        tree.Write("area/CLAUDE.md", "@AGENTS.md @../docs/extra.md");
        tree.Write("docs/extra.md", "extra");
        tree.Write("other/AGENTS.md", "not read from area");

        var files = InstructionChains.Files(tree.Root, tree.Path("area"));

        Assert.Equal(
            new[] { "AGENTS.md", "CLAUDE.md", "area/AGENTS.md", "area/CLAUDE.md", "docs/extra.md" }.Order(StringComparer.Ordinal),
            files.Select(tree.Relative).Order(StringComparer.Ordinal));
        Assert.Equal("root rules".Length + "@AGENTS.md\nclaude".Length + "area rules".Length
            + "@AGENTS.md @../docs/extra.md".Length + "extra".Length, InstructionChains.Bytes(tree.Root, tree.Path("area")));
    }

    [Fact]
    public void An_import_inside_code_or_to_a_missing_file_is_not_followed()
    {
        using var tree = new GuideTree();
        tree.Write("AGENTS.md", "Each CLAUDE.md imports `@notes.md`.\n```\n@notes.md\n```\nSee @missing.md.");
        tree.Write("notes.md", "never read");

        Assert.Equal(["AGENTS.md"], InstructionChains.Files(tree.Root, tree.Root).Select(tree.Relative));
    }

    [Fact]
    public void A_line_ending_counts_as_one_byte_as_git_keeps_it()
    {
        using var tree = new GuideTree();
        tree.Write("AGENTS.md", "one\r\ntwo\r\n");

        Assert.Equal("one\ntwo\n".Length, InstructionChains.Bytes(tree.Root, tree.Root));
    }

    [Fact]
    public void The_rule_flags_the_chain_that_goes_over_the_limit_and_skips_build_folders()
    {
        using var tree = new GuideTree();
        tree.Write("AGENTS.md", new string('r', 20));
        tree.Write("short/AGENTS.md", new string('s', 10));
        tree.Write("long/AGENTS.md", new string('l', 25));
        tree.Write("long/bin/AGENTS.md", new string('b', 100));
        tree.Write(".github/AGENTS.md", new string('g', 5));

        var oversized = InstructionChains.Oversized(tree.Root, 40);

        Assert.Equal(["long"], oversized.Select(chain => chain.Folder));
        Assert.Equal(45, oversized.Single().Bytes);
        Assert.Contains(tree.Path(".github"), InstructionChains.Folders(tree.Root));
    }

    private sealed class GuideTree : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("instruction-chains-").FullName;

        public string Path(string relative) => System.IO.Path.Combine(Root, relative);

        public string Relative(string path) => System.IO.Path.GetRelativePath(Root, path).Replace('\\', '/');

        public void Write(string relative, string content)
        {
            var file = Path(relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
