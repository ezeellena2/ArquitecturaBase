using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppAudioWebhookReaderTests
{
    private static JsonObject Audio() {
        var message = MetaWebhook.Text("wamid.audio", "5493413654813", "AR.1102953142229032", new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc), "ignored");
        message.Remove("text"); message["type"] = "audio";
        message["audio"] = new JsonObject { ["id"] = "12345", ["mime_type"] = "audio/ogg; codecs=opus", ["sha256"] = Convert.ToBase64String(new byte[32]), ["voice"] = true };
        return message;
    }
    [Fact]
    public void A_signed_audio_descriptor_keeps_media_identity_MIME_parameters_and_canonical_hash_without_text()
    {
        var result = Read(Audio());
        var message = Assert.Single(result.Messages);
        Assert.Equal(WhatsAppMessageKind.Audio, message.Kind);
        Assert.NotNull(message.Audio);
        Assert.Equal("12345", message.Audio.MediaId);
        Assert.Equal("audio/ogg; codecs=opus", message.Audio.ContentType);
        Assert.Equal(new string('0', 64), message.Audio.Sha256);
        Assert.Null(message.Body); Assert.Null(message.ReplyId);
        Assert.DoesNotContain("12345", message.Audio.ToString(), StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("id")]
    [InlineData("mime_type")]
    [InlineData("sha256")]
    [InlineData("malformedHash")]
    [InlineData("unsupported")]
    public void Incomplete_audio_is_preserved_as_unavailable_instead_of_dropping_the_message(string missing)
    {
        var value = Audio(); var audio = (JsonObject)value["audio"]!;
        if (missing == "malformedHash") audio["sha256"] = "invalid";
        else if (missing == "unsupported") audio["mime_type"] = "audio/mpeg";
        else audio.Remove(missing);
        var message = Assert.Single(Read(value).Messages);
        Assert.Equal(WhatsAppMessageKind.Audio, message.Kind);
        Assert.Null(message.Audio);
        Assert.Null(message.Body);
    }
    private static ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppWebhookBatch Read(JsonObject message)
    {
        var reader = new WhatsAppWebhookReader(Options.Create(new WhatsAppOptions { PhoneNumberId = ApiFactory.WhatsAppPhoneNumberId, AccessToken = "fictional-key" }), NullLogger<WhatsAppWebhookReader>.Instance);
        return reader.Read(MetaWebhook.Build(contacts: [MetaWebhook.Contact("5493413654813", "AR.1102953142229032", "Audio user")], messages: [message]));
    }
}
