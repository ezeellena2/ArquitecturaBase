using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;
public sealed class WhatsAppAssistantPresentationTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    [Fact]
    public void Explanation_uses_the_captured_reason_and_display_values_without_canonical_source_values()
    {
        var host=new WhatsAppAssistantPresentationFixture();var view=host.Policy.Explain(host.Row,true);
        Assert.NotNull(view);Assert.Equal("Contó una visita.",view.Reason);Assert.Equal("Sucursal Norte",Assert.Single(view.ProposedFields).DisplayValue);
        Assert.DoesNotContain("canonical-secret",JsonSerializer.Serialize(view),StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(false,1)]
    [InlineData(true,0)]
    [InlineData(true,-1)]
    public void Permission_or_source_expiry_retires_the_explanation(bool allowed,int minutes)
    {var host=new WhatsAppAssistantPresentationFixture();Assert.Null(host.Policy.Explain(host.Row with{ContentExpiresAtUtc=host.Context.Clock.GetUtcNow().UtcDateTime.AddMinutes(minutes)},allowed));}
    [Fact]
    public async Task Receipt_observation_checks_record_permission_each_time()
    {
        var host=new WhatsAppAssistantPresentationFixture();var actor=host.Context.Operator.Id;
        Assert.NotNull(await host.Policy.ReadAsync(actor,host.Row,Ct));host.Records.Allowed=false;
        Assert.Null(await host.Policy.ReadAsync(actor,host.Row,Ct));Assert.Equal(2,host.Records.Reads);
    }
    [Fact]
    public async Task Expired_content_is_not_decrypted_or_read_from_the_record_module()
    {var host=new WhatsAppAssistantPresentationFixture();Assert.Null(await host.Policy.ReadAsync(host.Context.Operator.Id,host.Row with{ContentExpiresAtUtc=host.Context.Clock.GetUtcNow().UtcDateTime},Ct));Assert.Equal(0,host.Records.Reads);}
    [Fact]
    public async Task Current_pointer_is_shared_and_withdrawn_without_send_permission()
    {
        var host=new WhatsAppAssistantPresentationFixture();var actor=host.Context.Operator.Id;
        Assert.Equal(host.Reader.Reference!.RequestId,(await host.Policy.FindCurrentAsync(host.Context.Contact.Id,actor,Ct))?.RequestId);
        host.Context.Permissions.Permissions[actor]=[WhatsAppPermissions.Read];
        Assert.Null(await host.Policy.FindCurrentAsync(host.Context.Contact.Id,actor,Ct));
    }
    [Theory]
    [InlineData(false,"[\"Visita\",\"Mantenimiento\"]")]
    [InlineData(true,"[{\"id\":\"private-button-id\",\"title\":\"Visita\",\"definitionRevision\":1},{\"id\":\"other-private-id\",\"title\":\"Mantenimiento\",\"definitionRevision\":2}]")]
    public void Quick_reply_history_exposes_captured_titles_only(bool outgoing,string json)
    {
        var titles=WhatsAppAssistantPresentationPolicy.Titles(json,outgoing);Assert.NotNull(titles);Assert.Equal(new[]{"Visita","Mantenimiento"},titles);
        Assert.DoesNotContain("private",JsonSerializer.Serialize(titles),StringComparison.Ordinal);
    }
}
