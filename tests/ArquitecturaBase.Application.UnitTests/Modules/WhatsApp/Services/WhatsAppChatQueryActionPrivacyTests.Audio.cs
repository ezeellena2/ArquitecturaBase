using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;
public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    private static WhatsAppMessageRow AudioRow(WhatsAppAudioState state)=>new(Guid.NewGuid(),WhatsAppMessageDirection.Inbound,WhatsAppMessageKind.Audio,"Texto original del audio",Now,1,null,null,null,
        Audio:new(state,true,Now.AddDays(90)));
    [Fact]
    public async Task Completed_audio_projects_transcript_and_private_playback_availability_without_starting_a_model()
    {
        var host=new Host();host.Reader.Audio=AudioRow(WhatsAppAudioState.Completed);
        var result=await host.Service.GetThreadAsync(new(host.Reader.Contact.Id),Guid.NewGuid(),Ct);
        Assert.True(result.IsSuccess);var message=Assert.Single(result.Value.Messages);Assert.NotNull(message.Audio);
        Assert.Equal(WhatsAppAudioState.Completed,message.Audio.State);Assert.True(message.Audio.CanPlay);Assert.Equal("Texto original del audio",message.Body);
    }
    [Fact]
    public async Task Audio_text_and_playback_are_hidden_at_source_expiry_before_daily_cleanup()
    {
        var host=new Host();host.Reader.Audio=AudioRow(WhatsAppAudioState.Completed);host.Clock.Advance(TimeSpan.FromDays(90));
        var result=await host.Service.GetThreadAsync(new(host.Reader.Contact.Id),Guid.NewGuid(),Ct);
        var message=Assert.Single(result.Value.Messages);Assert.Null(message.Body);Assert.NotNull(message.Audio);Assert.Equal(WhatsAppAudioState.Expired,message.Audio.State);Assert.False(message.Audio.CanPlay);
    }
    [Fact]
    public async Task A_pending_audio_never_exposes_a_transcript_from_an_unconfirmed_result()
    {
        var host=new Host();host.Reader.Audio=AudioRow(WhatsAppAudioState.Pending);
        var result=await host.Service.GetThreadAsync(new(host.Reader.Contact.Id),Guid.NewGuid(),Ct);
        Assert.Null(Assert.Single(result.Value.Messages).Body);
    }
    [Fact]
    public async Task Audio_list_preview_is_a_generic_label_and_never_the_transcript()
    {
        var host=new Host();host.Reader.AudioPreview=true;
        var result=await host.Service.ListAsync(new(),Guid.NewGuid(),Ct);
        Assert.True(result.IsSuccess);Assert.Equal("Audio",Assert.Single(result.Value.Page.Items).Preview);
    }
}
