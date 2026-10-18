using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppAudioFixture : IWhatsAppFormRecipientReader, IWhatsAppAudioReader, IWhatsAppAudioRepository, IWhatsAppMediaClient, IWhatsAppInboundSignal
{
    public static readonly DateTime Now = new(2026,10,17,12,0,0,DateTimeKind.Utc);
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(Now));
    public InMemoryUserAccounts Users { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakeSystemSettingsReader Settings { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public MemoryPrivateFileStore Files { get; } = new();
    public WhatsAppAudioDispatchService Dispatch { get; }
    public WhatsAppAudioService Service { get; }
    public WhatsAppAudioContentService Content { get; }
    public int Signals { get; private set; }
    public void Notify()=>Signals++;
    public WhatsAppAudioPresentationPolicy Presentation { get; }
    public WhatsAppAudioPreparationPolicy Preparation { get; }
    public int Downloads { get; private set; }
    public Action? BeforeDownload { get; set; }
    public Error? DownloadError { get; set; }
    public List<string> Locks { get; } = [];
    public Guid Actor { get; }
    public Guid Subject { get; }
    public WhatsAppContact Contact { get; } = WhatsAppContact.Create("5493413654813",null,"Audio test",Now);
    public WhatsAppMessage Message { get; }
    public WhatsAppAudioAccessPolicy Access { get; }
    private readonly byte[] sampleContent;
    public WhatsAppAudioFixture(byte[]? sample = null)
    {
        sampleContent = sample ?? Bytes();
        Actor=Users.AddUser("audio-operator@example.test").Id;
        Subject=Users.AddUser("audio-subject@example.test",phoneNumber:"+5493413654813").Id;
        Contact.LinkUser(Subject); Contact.ChangeMode(WhatsAppConversationMode.Manual); Current.UserId=Actor;
        Permissions.Permissions[Actor]=[WhatsAppPermissions.Read];
        Message=WhatsAppMessage.Inbound(Contact.Id,"wamid.audio.test",WhatsAppMessageKind.Audio,null,null,Now);
        Message.SetAudioDescriptor("12345","audio/ogg; codecs=opus",Convert.ToHexStringLower(SHA256.HashData(sampleContent)));
        Access=new(Users,Permissions,Current,this,Settings);
        Presentation=new(new WhatsAppContentRetention(90),Clock);
        Preparation=new(this,new WhatsAppAudioStoragePolicy(Files));
        Service=new(Access,this,this,new Conversations(this),UnitOfWork,Presentation,this,Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppAudioService>.Instance);
        Content=new(Access,this,new WhatsAppAudioStoragePolicy(Files),Presentation,Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppAudioContentService>.Instance);
        Dispatch=new(this,this,new Conversations(this),Preparation,Access,UnitOfWork,Presentation,Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppAudioDispatchService>.Instance);
    }
    public static byte[] Bytes()
    {
        var bytes=new byte[47]; "OggS"u8.CopyTo(bytes); bytes[26]=1;bytes[27]=19;
        "OpusHead"u8.CopyTo(bytes.AsSpan(28));bytes[36]=1;bytes[37]=1;return bytes;
    }
    public WhatsAppAudioSourceRow Row()=>new()
    {
        MessageId=Message.Id,ContactId=Contact.Id,OccurredAtUtc=Message.OccurredAtUtc,Mode=Contact.Mode,LinkedUserId=Contact.UserId,LinkRevision=Contact.LinkRevision,
        State=Message.AudioState!.Value,MediaId=Message.AudioMediaId,ContentType=Message.AudioContentType,Sha256=Message.AudioSha256,StorageKey=Message.AudioStorageKey,Size=Message.AudioSize,
        RequestId=Message.AudioRequestId,ActorUserId=Message.AudioActorUserId,ContextJson=Message.AudioContextJson,CapturedLinkRevision=Message.AudioLinkRevision,ContentExpiresAtUtc=Message.AudioContentExpiresAtUtc,
    };
    public async Task<WhatsAppFormRecipientRow?> FindAsync(Guid id,CancellationToken ct)
    {
        if(id!=Contact.Id)return null;var user=Contact.UserId is {} userId?await Users.FindByIdAsync(userId,ct):null;
        return new(id,Contact.LinkRevision,Contact.WaId,Contact.UserIdentifier,user?.Id,user?.DisplayName,user?.Email,user?.IsActive??false,user?.Culture,null,"");
    }

    public Guid Pin(bool automatic=false)
    {
        var context=new AssistantChannelContext(automatic?"whatsapp-assistant":"whatsapp-manual",automatic?Subject:Actor,Subject,"es",Settings.Presentation.DefaultTimeZoneId,
            new(automatic?[]:[WhatsAppPermissions.Read],[]),[],[]);
        return Message.RequestAudioNormalization(context.ActorUserId,JsonSerializer.Serialize(context,JsonSerializerOptions.Web),Contact.LinkRevision,Now.AddDays(90),Now).Value;
    }
    public Task<WhatsAppAudioSourceRow?> FindPendingAsync(Guid id,DateTime now,CancellationToken ct)=>Task.FromResult<WhatsAppAudioSourceRow?>(id==Contact.Id && Message.AudioState is WhatsAppAudioState.Pending or WhatsAppAudioState.Transcribing?Row():null);
    public Task<WhatsAppAudioSourceRow?> FindAsync(Guid id,Guid messageId,CancellationToken ct)=>Task.FromResult<WhatsAppAudioSourceRow?>(id==Contact.Id && messageId==Message.Id?Row():null);
    public Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime now,CancellationToken ct)=>throw new NotSupportedException();
    public Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> keys,DateTime cutoff,CancellationToken ct)=>throw new NotSupportedException();
    public Task<WhatsAppMessage?> GetForUpdateAsync(Guid contactId,Guid messageId,CancellationToken ct)
    { Assert.True(UnitOfWork.InTransaction);Locks.Add("message");return Task.FromResult<WhatsAppMessage?>(contactId==Contact.Id && messageId==Message.Id?Message:null); }
    public Task<WhatsAppMessage?> GetByRequestForUpdateAsync(Guid contactId,Guid requestId,CancellationToken ct)=>GetForUpdateAsync(contactId,Message.AudioRequestId==requestId?Message.Id:Guid.Empty,ct);
    public Task<Result<WhatsAppMediaContent>> DownloadAsync(string id,string mime,string sha,CancellationToken ct)
    {
        Assert.False(UnitOfWork.InTransaction);Downloads++;BeforeDownload?.Invoke();
        return Task.FromResult(DownloadError is {} error?Result.Failure<WhatsAppMediaContent>(error):Result.Success(new WhatsAppMediaContent(sampleContent)));
    }
    private sealed class Conversations(WhatsAppAudioFixture f):IWhatsAppConversationRepository
    {
        public Task<WhatsAppContact?> GetForUpdateAsync(Guid id,CancellationToken ct)
        {Assert.True(f.UnitOfWork.InTransaction);f.Locks.Add("contact");return Task.FromResult<WhatsAppContact?>(id==f.Contact.Id?f.Contact:null);}
        public Task<WhatsAppReadState?> GetReadStateAsync(Guid c,Guid a,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid c,Guid a,Guid r,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<bool> ExistsUnresolvedOutgoingAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppAssistantRequest>> ListPendingAutomaticRequestsAsync(Guid contactId, CancellationToken ct) => Task.FromResult<IReadOnlyList<WhatsAppAssistantRequest>>([]);
        public Task<bool> ExistsStartedAssistantMessageAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime d,CancellationToken ct)=>throw new NotSupportedException();
        public void Add(WhatsAppReadState state)=>throw new NotSupportedException();
        public void Add(WhatsAppOutgoingMessage outgoing)=>throw new NotSupportedException();
    }
}
