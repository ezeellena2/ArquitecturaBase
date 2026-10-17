using System.Reflection;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed class WhatsAppAssistantOriginQueryTests
{
    [Fact]
    public void Captured_origin_selects_the_session_sender_kind_instead_of_attributing_every_reply_to_an_operator()
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppRecordOriginReader(db,null!);
        var query=Assert.IsAssignableFrom<IQueryable>(typeof(WhatsAppRecordOriginReader).GetMethod("Rows",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(reader,[new[]{Guid.NewGuid()}]));
        var sql=query.ToQueryString();Assert.Contains("'Assistant'",sql,StringComparison.Ordinal);Assert.Contains("CASE",sql,StringComparison.Ordinal);
        Assert.Contains("OperatorUserId",sql,StringComparison.Ordinal);Assert.Contains("WhatsAppFlowSessions",sql,StringComparison.Ordinal);
    }
}
