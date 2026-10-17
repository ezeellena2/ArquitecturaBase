using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppAssistantContextFixture
{
    public static readonly DateTimeOffset Now = new(2026,10,17,15,0,0,TimeSpan.Zero);
    public FakeTimeProvider Clock { get; } = new(Now);
    public InMemoryUserAccounts Users { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakeSystemSettingsReader Settings { get; } = new();
    public UserAccount Subject { get; }
    public UserAccount Operator { get; }
    public WhatsAppContact Contact { get; } = WhatsAppContact.Create("5493413654813",null,"Fictitious assistant contact",Now.UtcDateTime);
    public IWhatsAppFormRecipientReader Recipients { get; }
    public WhatsAppAssistantAccessPolicy Policy { get; }
    public WhatsAppAssistantContextFixture()
    {
        Subject = Users.AddUser("assistant-subject@example.test",phoneNumber:"+5493413654813");
        Operator = Users.AddUser("assistant-operator@example.test");
        Contact.LinkUser(Subject.Id); Current.UserId = Operator.Id;
        Permissions.Permissions[Operator.Id] = [WhatsAppPermissions.Read,WhatsAppPermissions.Send];
        Settings.Presentation = Settings.Presentation with { DefaultTimeZoneId = "America/New_York" };
        Recipients = new Recipient(this);
        Policy = new(Users,Permissions,Current,Recipients,
            new WhatsAppFormActionPolicy(Users,new FakePhoneNumberParser(),new FakeWhatsAppAvailability(true,true),Clock),Settings,Clock);
    }
    private sealed class Recipient(WhatsAppAssistantContextFixture host) : IWhatsAppFormRecipientReader
    {
        public async Task<WhatsAppFormRecipientRow?> FindAsync(Guid id,CancellationToken ct)
        {
            if(id!=host.Contact.Id) return null;
            var account=host.Contact.UserId is {} userId ? await host.Users.FindByIdAsync(userId,ct) : null;
            return new(host.Contact.Id,host.Contact.LinkRevision,host.Contact.WaId,host.Contact.UserIdentifier,account?.Id,account?.DisplayName,account?.Email,
                account?.IsActive??false,account?.Culture,"account-revision","roles-revision");
        }
    }
}
