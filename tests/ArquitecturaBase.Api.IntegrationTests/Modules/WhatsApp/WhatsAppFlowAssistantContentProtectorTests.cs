using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.DataProtection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFlowAssistantContentProtectorTests
{
    [Fact]
    public void Private_explanation_and_proposed_values_round_trip_only_inside_protected_content()
    {
        var host=new Fixture();var bytes=host.Protect();
        Assert.DoesNotContain("Private explanation",Encoding.UTF8.GetString(bytes),StringComparison.Ordinal);
        Assert.DoesNotContain("private-value",Encoding.UTF8.GetString(bytes),StringComparison.Ordinal);
        var value=host.Protector.ReadAssistant(host.Session,host.Contact,host.User,host.Version,bytes);
        Assert.Equal("Private explanation",value.Origin.Reason);Assert.Equal("private-value",Assert.Single(value.Origin.ProposedValues).Value);
        Assert.Equal(host.Field,Assert.Single(value.ProposedFields).FieldId);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void A_protected_explanation_cannot_be_moved_to_another_owner(int changed)
    {
        var host=new Fixture();var bytes=host.Protect();
        Assert.Throws<CryptographicException>(()=>host.Protector.ReadAssistant(changed==0?Guid.NewGuid():host.Session,
            changed==1?Guid.NewGuid():host.Contact,changed==2?Guid.NewGuid():host.User,changed==3?Guid.NewGuid():host.Version,bytes));
    }
    [Fact]
    public void Assistant_content_and_legacy_tokens_have_separate_protection_purposes()
    {
        var host=new Fixture();var bytes=host.Protect();
        Assert.Throws<CryptographicException>(()=>host.Protector.Read(host.Session,host.Contact,host.User,host.Version,bytes));
    }
    private sealed class Fixture
    {
        public Guid Session {get;}=Guid.NewGuid();public Guid Contact {get;}=Guid.NewGuid();public Guid User {get;}=Guid.NewGuid();
        public Guid Version {get;}=Guid.NewGuid();public Guid Field {get;}=Guid.NewGuid();
        public WhatsAppFlowSessionProtector Protector {get;}=new(new EphemeralDataProtectionProvider());
        public byte[] Protect()=>Protector.ProtectAssistant(Session,Contact,User,Version,new(1,
            new(StructuredRecordSenderKind.Manual,Guid.NewGuid(),"Private explanation",null,[new(Field,"shortText","private-value")],[Field]),
            [new(Field,"Field","private-value")]));
    }
}
