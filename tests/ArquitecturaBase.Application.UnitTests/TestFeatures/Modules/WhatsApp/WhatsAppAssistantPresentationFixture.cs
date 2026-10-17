using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
internal sealed class WhatsAppAssistantPresentationFixture
{
    public WhatsAppAssistantContextFixture Context {get;}=new();
    public FakeWhatsAppAssistantReader Reader {get;}=new();
    public WhatsAppAssistantSuggestionFixture.SessionProtector Protector {get;}=new();
    public RecordSource Records {get;}=new();
    public WhatsAppAssistantPresentationRow Row {get;}
    public WhatsAppAssistantPresentationPolicy Policy {get;}
    public Guid FieldId {get;}=Guid.NewGuid();
    public WhatsAppAssistantPresentationFixture()
    {
        Row=new(Guid.NewGuid(),Context.Contact.Id,Context.Subject.Id,Guid.NewGuid(),[2],Context.Clock.GetUtcNow().UtcDateTime.AddMinutes(10));
        Protector.Content=new(1,new(StructuredRecordSenderKind.Manual,Context.Operator.Id,"Contó una visita.",null,
            [new(FieldId,"shortText","canonical-secret")],[]),[new(FieldId,"Lugar","Sucursal Norte")]);
        Reader.Reference=new(Guid.NewGuid(),Context.Contact.Id,Guid.NewGuid(),Context.Operator.Id,Context.Subject.Id,
            WhatsAppAssistantRequestKind.Manual,Context.Contact.ModeRevision,Context.Contact.LinkRevision,"{}",Row.ContentExpiresAtUtc,
            WhatsAppAssistantRequestState.Ready,Guid.NewGuid(),Guid.NewGuid(),null);
        Policy=new(Protector,Reader,Records,Context.Permissions,Context.Clock);
    }
    internal sealed class RecordSource : IStructuredRecordSource
    {
        public bool Allowed {get;set;}=true;
        public int Reads {get;private set;}
        public Task<StructuredRecordReadResult> ReadAsync(Guid actor,string culture,IReadOnlyCollection<Guid> ids,CancellationToken ct)
        {ct.ThrowIfCancellationRequested();Reads++;Assert.Empty(ids);return Task.FromResult(new StructuredRecordReadResult(Allowed,new Dictionary<Guid,StructuredRecordReadReply>()));}
    }
}
