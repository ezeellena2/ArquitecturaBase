namespace ArquitecturaBase.Application.Models.SystemQueries;

public sealed record SystemQueryDefinition(string Id, int Version, string Description,
    string ParametersJson, IReadOnlyList<string> RequiredPermissions, int MaximumRows = 50, int MaximumBytes = 16384);
