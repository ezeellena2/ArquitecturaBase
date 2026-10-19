using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task A_completed_reply_keeps_the_owners_whole_version_name_with_its_values(string original, string ui)
    {
        using var culture = new CultureScope(ui);
        var host = new Host();
        var receiptId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var names = JsonSerializer.Serialize(new Dictionary<string, string> { [original] = "Original form", [ui] = "Partial form" });
        host.Reader.Audio = ReplyRow(new("Completed", receiptId, definitionId, versionId, 1, names, original, Guid.NewGuid(), "VIS-000001"));
        host.Source.Replies[receiptId] = new(receiptId, Guid.NewGuid(), "VIS-000001", definitionId, versionId, 1, "Original form",
            [new(Guid.NewGuid(), "place", "shortText", "Original question", "Business answer")]);

        var result = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);

        var reply = Assert.Single(result.Value.Messages).FormReply!;
        Assert.Equal("Original form", reply.Name);
        Assert.Equal("Original question", Assert.Single(reply.Values!).Label);
        Assert.Equal(ui, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    [Theory]
    [InlineData("es", "en", "Pending", false)]
    [InlineData("es", "en", "Rejected", true)]
    [InlineData("en", "es", "Pending", true)]
    [InlineData("en", "es", "Rejected", false)]
    public async Task A_recognized_unconfirmed_reply_has_its_session_name_when_the_UI_translation_is_missing(string original, string ui, string state, bool emptySecondary)
    {
        using var culture = new CultureScope(ui);
        var host = new Host();
        var names = new Dictionary<string, string> { [original] = "Original form" };
        if (emptySecondary) names[ui] = "";
        host.Reader.Audio = ReplyRow(new(state, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            JsonSerializer.Serialize(names), original, null, null));

        var result = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);

        var reply = Assert.Single(result.Value.Messages).FormReply!;
        Assert.Equal("Original form", reply.Name);
        Assert.Null(reply.Values);
    }

    [Fact]
    public async Task Losing_record_read_keeps_only_the_safe_session_name_in_the_reply_card()
    {
        using var culture = new CultureScope("en");
        var host = new Host();
        var receiptId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        host.Reader.Audio = ReplyRow(new("Completed", receiptId, definitionId, versionId, 1,
            "{\"es\":\"Original form\",\"en\":\"\"}", "es", Guid.NewGuid(), "VIS-000001"));
        host.Source.Replies[receiptId] = new(receiptId, Guid.NewGuid(), "VIS-000001", definitionId, versionId, 1,
            "Private projection", [new(Guid.NewGuid(), "place", "shortText", "Private question", "Private answer")]);
        host.Source.Allowed = false;

        var result = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);

        var reply = Assert.Single(result.Value.Messages).FormReply!;
        Assert.False(result.Value.CanReadFormRecords);
        Assert.Equal("Original form", reply.Name);
        Assert.Null(reply.Values);
    }
    [Theory]
    [InlineData("Pending")]
    [InlineData("Rejected")]
    [InlineData("Completed")]
    public async Task A_reply_without_authorized_values_keeps_the_sent_session_name_instead_of_a_partial_UI_translation(string state)
    {
        using var culture = new CultureScope("en");
        var host = new Host();
        host.Source.Allowed = false;
        host.Reader.Audio = ReplyRow(new(state, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            "{\"es\":\"Original form\",\"en\":\"Partial form\"}", "es", null, null));

        var result = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);

        var reply = Assert.Single(result.Value.Messages).FormReply!;
        Assert.Equal("Original form", reply.Name);
        Assert.Null(reply.Values);
    }
    private static WhatsAppMessageRow ReplyRow(WhatsAppFormReplyRow reply) => new(Guid.NewGuid(),
        WhatsAppMessageDirection.Inbound, WhatsAppMessageKind.FlowReply, null, Now, 1, null, null, null, FormReply: reply);
}