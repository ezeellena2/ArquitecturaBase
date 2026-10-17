using System.Reflection;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed class WhatsAppFormActionReadQueryTests
{
    [Fact]
    public void Local_observation_joins_receipts_without_requiring_outbound_history_and_keeps_legacy_event_dates()
    {
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppFormActionReader(db);var method=typeof(WhatsAppFormActionReader).GetMethod("QueryObservations",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var query=Assert.IsAssignableFrom<IQueryable<WhatsAppFormActionObservationRow>>(method.Invoke(reader,[new[]{Guid.NewGuid()},new[]{Guid.NewGuid()}]));
        var sql=query.ToQueryString();Assert.Contains("LEFT JOIN",sql,StringComparison.Ordinal);Assert.Contains("WhatsAppDeliveryReceipts",sql,StringComparison.Ordinal);
        Assert.Contains("DeliveredAtUtc",sql,StringComparison.Ordinal);Assert.Contains("ReadAtUtc",sql,StringComparison.Ordinal);Assert.Contains("OccurredAtUtc",sql,StringComparison.Ordinal);
        Assert.Contains("CASE",sql,StringComparison.Ordinal);Assert.Contains("FormAction",sql,StringComparison.Ordinal);Assert.DoesNotContain("\"WhatsAppMessages\"",sql,StringComparison.Ordinal);
    }
}
