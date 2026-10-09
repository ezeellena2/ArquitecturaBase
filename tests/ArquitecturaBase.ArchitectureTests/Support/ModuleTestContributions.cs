namespace ArquitecturaBase.ArchitectureTests.Support;

internal interface IModuleArchitectureContribution
{
    IReadOnlyCollection<string> LockOwners { get; }
    IReadOnlyCollection<string> LockPrefixes { get; }
    IReadOnlyCollection<string> BulkUpdateOwners { get; }
}

internal static class ModuleTestContributions
{
    public static IReadOnlyCollection<IModuleArchitectureContribution> All { get; } =
        [.. typeof(ModuleTestContributions).Assembly.GetTypes()
            .Where(x => x.IsClass && !x.IsAbstract && typeof(IModuleArchitectureContribution).IsAssignableFrom(x))
            .OrderBy(x => x.FullName, StringComparer.Ordinal)
            .Select(x => (IModuleArchitectureContribution)Activator.CreateInstance(x, nonPublic: true)!)];
}
