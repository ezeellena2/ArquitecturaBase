using System.Text.RegularExpressions;
using Npgsql;

namespace AssistantQualification;

internal sealed record QualificationSettings(string RunId, string RepositoryRoot, string OutputDirectory,
    string SourceConnectionString, string EvaluationConnectionString, string RedisConnectionString)
{
    public static QualificationSettings FromEnvironment(string runId, string repositoryRoot, string outputDirectory)
    {
        if (!Regex.IsMatch(runId, "^[a-z0-9_]{8,40}$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Invalid qualification run identifier.");
        var source = Required("QUALIFICATION_SOURCE_APPDB");
        var evaluation = Required("QUALIFICATION_EVALUATION_APPDB");
        var redis = Required("QUALIFICATION_CACHE");
        var sourceDb = new NpgsqlConnectionStringBuilder(source);
        var evaluationDb = new NpgsqlConnectionStringBuilder(evaluation);
        if (!string.Equals(evaluationDb.Database, "assistant_qualification_" + runId, StringComparison.Ordinal)
            || string.Equals(sourceDb.Database, evaluationDb.Database, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Evaluation database isolation check failed.");
        return new(runId, Path.GetFullPath(repositoryRoot), Path.GetFullPath(outputDirectory), source, evaluation, redis);
    }

    public QualificationSettings ForReadOnlyDiagnostic()
    {
        var evaluation = new NpgsqlConnectionStringBuilder(EvaluationConnectionString) { Options = "-c default_transaction_read_only=on" };
        return this with { EvaluationConnectionString = evaluation.ConnectionString };
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException("Required qualification setting is missing.");

    public override string ToString() => "QualificationSettings { connections: [REDACTED] }";
}
