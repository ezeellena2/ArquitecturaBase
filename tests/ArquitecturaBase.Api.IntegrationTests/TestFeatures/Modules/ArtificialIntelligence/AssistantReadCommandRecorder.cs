using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AssistantReadCommandRecorder : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];
    // I20: the statements with a clone of their parameters, so a test can EXPLAIN exactly what ran.
    public List<AssistantRecordedCommand> Statements { get; } = [];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        Statements.Add(new(command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(parameter => parameter.Clone()).ToArray()));
        return ValueTask.FromResult(result);
    }
}

internal sealed record AssistantRecordedCommand(string Sql, IReadOnlyList<NpgsqlParameter> Parameters);
