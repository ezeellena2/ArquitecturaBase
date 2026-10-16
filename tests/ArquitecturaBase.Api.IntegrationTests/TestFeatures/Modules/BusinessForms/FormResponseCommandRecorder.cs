using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormResponseCommandRecorder : DbCommandInterceptor
{
    public List<FormResponseRecordedCommand> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(new(command.CommandText, command.Parameters.Cast<NpgsqlParameter>().Select(parameter => parameter.Clone()).ToArray()));
        return ValueTask.FromResult(result);
    }
}

internal sealed record FormResponseRecordedCommand(string Sql, IReadOnlyList<NpgsqlParameter> Parameters);
