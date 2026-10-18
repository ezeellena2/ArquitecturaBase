using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantOfferQueryTests
{
    [Theory]
    [InlineData("QuickReplies")]
    [InlineData("Failed")]
    [InlineData("\"LinkRevision\"")]
    [InlineData("\"UserId\"")]
    public void Quick_reply_metadata_is_bound_to_the_current_subject_and_link_in_SQL(string expected)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new WhatsAppAssistantReader(db, Options.Create(new WhatsAppMessageRetentionOptions()),
            new FakeTimeProvider(new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc)));
        var method = typeof(WhatsAppAssistantReader).GetMethod("QuestionRows", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, [Guid.NewGuid(), new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc)]));
        var sql = query.ToQueryString(); Assert.Contains(expected, sql, StringComparison.Ordinal);
        Assert.Contains("sinceUtc", sql, StringComparison.Ordinal);
        Assert.Contains("QuestionOutgoingId", sql, StringComparison.Ordinal); Assert.Contains("SubjectUserId", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_quick_reply_uses_the_original_question_request_and_its_own_published_version()
    {
        var request = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid(); var firstVersion = Guid.NewGuid(); var secondVersion = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new[] { new WhatsAppOutgoingQuickReply(WhatsAppAssistantReplyCodec.Encode(request, first, firstVersion), "Visita", 1),
            new WhatsAppOutgoingQuickReply(WhatsAppAssistantReplyCodec.Encode(request, second, secondVersion), "Mantenimiento", 2) });
        var rows = WhatsAppAssistantReader.ReadQuickReplies(request, json);
        Assert.Equal(2, rows.Length); Assert.All(rows, row => Assert.Equal(request, row.RequestId));
        Assert.Equal(first, rows[0].DefinitionId); Assert.Equal(firstVersion, rows[0].VersionId); Assert.Equal("Visita", rows[0].Name);
        Assert.Equal(second, rows[1].DefinitionId); Assert.Equal(secondVersion, rows[1].VersionId); Assert.Equal("Mantenimiento", rows[1].Name);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("cross-request")]
    [InlineData("json")]
    [InlineData("empty")]
    public void Invalid_stored_button_metadata_is_not_converted_into_an_empty_offer_history(string invalid)
    {
        var request = Guid.NewGuid();
        var id = invalid == "malformed" ? "untrusted" : WhatsAppAssistantReplyCodec.Encode(invalid == "cross-request" ? Guid.NewGuid() : request, Guid.NewGuid(), Guid.NewGuid());
        var json = invalid switch { "json" => "{", "empty" => "[]", _ => JsonSerializer.Serialize(new[] { new WhatsAppOutgoingQuickReply(id, "Visita", 1) }) };
        Assert.Throws<InvalidOperationException>(() => WhatsAppAssistantReader.ReadQuickReplies(request, json));
    }
}
