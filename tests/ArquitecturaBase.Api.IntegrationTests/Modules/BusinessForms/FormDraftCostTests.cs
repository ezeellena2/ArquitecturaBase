using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task A_forty_by_fifty_draft_has_bounded_protected_cost_and_small_expired_metadata()
    {
        using var client = await AdminAsync(); var payload = Payload("Cost budget proof"); var fields = new JsonArray();
        for (var i = 0; i < 40; i++)
        {
            var choices = new JsonArray(); for (var j = 0; j < 50; j++) choices.Add(new JsonObject
                { ["localId"] = "o" + j, ["label"] = new JsonObject { ["es"] = "Option " + j + " with a representative label" } });
            fields.Add(new JsonObject { ["localId"] = "f" + i, ["type"] = "choice", ["label"] = new JsonObject { ["es"] = "Question " + i }, ["options"] = choices });
        }
        payload["draft"]!["fields"] = fields;
        var inputBytes = JsonSerializer.SerializeToUtf8Bytes(payload).Length; Assert.InRange(inputBytes, 1, 524288);
        var saved = await CreateAsync(client, payload); var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await using var connection = new NpgsqlConnection(factory.ConnectionString); await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT pg_column_size(d.\"SchemaJson\"), octet_length(c.\"ProtectedResult\"), pg_column_size(c) FROM \"FormDefinitions\" d JOIN \"FormDraftChanges\" c ON c.\"DefinitionId\"=d.\"Id\" WHERE d.\"Id\"=@id", connection);
        command.Parameters.AddWithValue("id", id);
        int schemaBytes; int protectedBytes; int receiptBytes;
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        { Assert.True(await reader.ReadAsync(Ct)); schemaBytes = reader.GetInt32(0); protectedBytes = reader.GetInt32(1); receiptBytes = reader.GetInt32(2); }
        Assert.InRange(protectedBytes, 1, 1048576);
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow().AddDays(31));
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock))));
        await using (var scope = host.Services.CreateAsyncScope()) Assert.True((await scope.ServiceProvider.GetRequiredService<IFormDraftRetentionService>().PurgeExpiredResultsAsync(Ct)).IsSuccess);
        await using var tombstone = new NpgsqlCommand("SELECT pg_column_size(c) FROM \"FormDraftChanges\" c WHERE c.\"DefinitionId\"=@id AND c.\"ProtectedResult\" IS NULL", connection);
        tombstone.Parameters.AddWithValue("id", id); var metadataBytes = (int)(await tombstone.ExecuteScalarAsync(Ct))!; Assert.InRange(metadataBytes, 1, 512);
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "business-forms-cost.txt"),
            $"40 fields x 50 choices; request={inputBytes}; schemaColumn={schemaBytes}; protectedResult={protectedBytes}; receiptRecord={receiptBytes}; tombstoneRecord={metadataBytes}; units=bytes; excludes indexes and TOAST overhead", Ct);
    }
}
