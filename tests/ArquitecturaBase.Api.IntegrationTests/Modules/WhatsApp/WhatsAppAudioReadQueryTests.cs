using System.Reflection;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed class WhatsAppAudioReadQueryTests
{
    [Theory]
    [InlineData("Pending")]
    [InlineData("Rows")]
    public void Audio_projection_and_pending_selection_translate_without_opening_a_database(string methodName)
    {
        var now=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader=new WhatsAppAudioReader(db,new(90),new FakeTimeProvider(now));
        var method=typeof(WhatsAppAudioReader).GetMethod(methodName,BindingFlags.NonPublic|BindingFlags.Instance)!;
        var query=Assert.IsAssignableFrom<IQueryable<WhatsAppAudioSourceRow>>(method.Invoke(reader,methodName=="Pending"?[now]:null));
        query=query.Where(row=>row.ContactId==Guid.Empty).OrderBy(row=>row.OccurredAtUtc).ThenBy(row=>row.MessageId).Take(50);
        string? sql=null;var exception=Record.Exception(()=>sql=query.ToQueryString());Assert.Null(exception);Assert.NotNull(sql);
        Assert.Contains("INNER JOIN",sql,StringComparison.Ordinal);Assert.Contains("Audio",sql,StringComparison.Ordinal);Assert.Contains("LIMIT",sql,StringComparison.Ordinal);
        if(methodName=="Pending"){Assert.Contains("OccurredAtUtc",sql,StringComparison.Ordinal);Assert.Contains("AudioContentExpiresAtUtc",sql,StringComparison.Ordinal);Assert.Contains("Assistant",sql,StringComparison.Ordinal);Assert.Contains("\"AudioStorageKey\" IS NULL",sql,StringComparison.Ordinal);}
    }
}
