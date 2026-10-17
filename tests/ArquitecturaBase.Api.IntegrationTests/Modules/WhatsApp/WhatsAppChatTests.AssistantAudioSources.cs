using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;
public sealed partial class WhatsAppChatTests
{
    [Fact]
    public async Task Completed_audio_is_a_source_with_its_own_identity_text_and_shortest_lifetime()
    {
        var row=await AddAssistantAudioSourceAsync("completed",automatic:true);
        var source=await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().FindSourceAsync(row.Contact,row.Audio,Ct));
        Assert.NotNull(source);Assert.Equal(row.Audio,source.Id);Assert.Equal("Visité la sucursal norte.",source.Body);Assert.Equal(row.Expiry,source.ContentExpiresAtUtc);
        var latest=await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().FindLatestSourceAsync(row.Contact,Ct));
        Assert.Equal(row.Audio,latest?.Id);
        var pending=await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().ListPendingContactsAsync([],1000,Now,Ct));Assert.Contains(row.Contact,pending);
        var message=await factory.ExecuteDbContextAsync(db=>db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(item=>item.Id==row.Audio,Ct));Assert.Equal(WhatsAppMessageKind.Audio,message.Kind);
    }
    [Theory]
    [InlineData("pending")]
    [InlineData("expired")]
    public async Task A_later_unusable_audio_blocks_prior_text_and_manual_suggestion(string state)
    {
        var row=await AddAssistantAudioSourceAsync(state,automatic:false);
        Assert.Null(await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().FindLatestSourceAsync(row.Contact,Ct)));
        Assert.Null(await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().FindSourceAsync(row.Contact,row.Audio,Ct)));
        Assert.Null(await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().FindCurrentAsync(row.Contact,Ct)));
    }
    [Theory]
    [InlineData("pending")]
    [InlineData("expired")]
    public async Task Automatic_scan_does_not_fall_back_to_prior_text_behind_unusable_audio(string state)
    {
        var row=await AddAssistantAudioSourceAsync(state,automatic:true);
        var pending=await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().ListPendingContactsAsync([],1000,Now,Ct));Assert.DoesNotContain(row.Contact,pending);
    }
    private async Task<(Guid Contact,Guid Audio,DateTime Expiry)> AddAssistantAudioSourceAsync(string state,bool automatic)
    {
        var actor=await OperatorAsync(WhatsAppPermissions.Read);
        return await factory.InTransactionAsync(async services=>
        {
            var db=services.GetRequiredService<ApplicationDbContext>();var contact=WhatsAppContact.Create(MetaWebhook.UniqueWaId(),null,"Audio source fixture",Now.AddHours(-3));contact.LinkUser(actor);
            if(automatic)contact.ChangeMode(WhatsAppConversationMode.Assistant);
            var text=WhatsAppMessage.Inbound(contact.Id,MetaWebhook.UniqueWaMessageId(),WhatsAppMessageKind.Text,"Older source",null,Now.AddHours(-4));text.SetInboundSequence(1);text.Classify(WhatsAppMessagePurpose.Business);text.MarkProcessed(Now.AddHours(-4));
            var audio=WhatsAppMessage.Inbound(contact.Id,MetaWebhook.UniqueWaMessageId(),WhatsAppMessageKind.Audio,null,null,Now.AddHours(-3));audio.SetInboundSequence(2);audio.SetAudioDescriptor("12345","audio/ogg",new string('a',64));audio.StoreAudio("whatsapp/audio/"+audio.Id.ToString("N")+".ogg",100);audio.Classify(WhatsAppMessagePurpose.Business);audio.MarkProcessed(Now.AddHours(-3));
            var expiry=state=="expired"?Now.AddHours(-1):Now.AddMinutes(30);
            var requested=audio.RequestAudioNormalization(actor,"{}",contact.LinkRevision,expiry,Now.AddHours(-3));Assert.True(requested.IsSuccess);
            if(state!="pending")Assert.True(audio.CompleteAudio(requested.Value,"Visité la sucursal norte.",Now.AddHours(-2)).IsSuccess);
            db.AddRange(contact,text,audio);
            if(!automatic)
            {
                var reference=WhatsAppAssistantRequest.Create(Guid.NewGuid(),contact.Id,text.Id,actor,actor,WhatsAppAssistantRequestKind.Manual,contact.ModeRevision,contact.LinkRevision,"{}",Now.AddMinutes(-1),Now.AddHours(1));Assert.True(reference.Ready(Guid.NewGuid(),null,Now));db.Add(reference);
            }
            await Task.CompletedTask;return(contact.Id,audio.Id,expiry);
        });
    }
}
