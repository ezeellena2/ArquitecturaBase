using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAssistantReplyCodecTests
{
    [Fact]
    public void A_reply_identifies_only_its_frozen_request_and_form_version_within_provider_limits()
    {
        var request=Guid.NewGuid();var definition=Guid.NewGuid();var version=Guid.NewGuid();
        var token=WhatsAppAssistantReplyCodec.Encode(request,definition,version);
        Assert.Equal($"FORM_ASSISTANT:{request:N}:{definition:N}:{version:N}",token);Assert.True(token.Length<=256);
        Assert.True(WhatsAppAssistantReplyCodec.TryParse(token,out var actualRequest,out var actualDefinition,out var actualVersion));
        Assert.Equal(request,actualRequest);Assert.Equal(definition,actualDefinition);Assert.Equal(version,actualVersion);
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("ACCESS_LOGIN")] [InlineData("FORM_ASSISTANT:invalid")]
    public void Unrelated_and_malformed_reply_ids_do_not_become_form_authority(string? value)
    {
        Assert.False(WhatsAppAssistantReplyCodec.TryParse(value,out var request,out var definition,out var version));
        Assert.Equal(Guid.Empty,request);Assert.Equal(Guid.Empty,definition);Assert.Equal(Guid.Empty,version);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void An_empty_owner_reference_is_not_an_encodable_or_parseable_reply(int empty)
    {
        var request=empty==0?Guid.Empty:Guid.NewGuid();var definition=empty==1?Guid.Empty:Guid.NewGuid();var version=empty==2?Guid.Empty:Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(()=>WhatsAppAssistantReplyCodec.Encode(request,definition,version));
        Assert.False(WhatsAppAssistantReplyCodec.TryParse($"FORM_ASSISTANT:{request:N}:{definition:N}:{version:N}",out _,out _,out _));
    }
}
