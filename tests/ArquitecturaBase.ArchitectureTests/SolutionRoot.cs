namespace ArquitecturaBase.ArchitectureTests;

internal static class SolutionRoot
{
    public static string FullPath { get; } = Find();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ArquitecturaBase.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("ArquitecturaBase.slnx was not found.");
    }
}
