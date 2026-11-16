using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormRecordReceivedDateIndexTests(ApiFactory factory)
{
    [Fact]
    public async Task Current_responses_are_indexed_by_form_and_received_date()
    {
        var definition = await factory.ExecuteScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            return await db.Database.SqlQueryRaw<string>("""
                SELECT indexdef AS "Value" FROM pg_indexes
                WHERE indexname = 'IX_BusinessFormRecords_Current_DefinitionId_SubmittedAtUtc'
                """).SingleAsync(TestContext.Current.CancellationToken);
        });

        Assert.Contains("(\"DefinitionId\", \"SubmittedAtUtc\")", definition);
        Assert.Contains("WHERE (\"ReplacedByRecordId\" IS NULL)", definition);
    }
}
