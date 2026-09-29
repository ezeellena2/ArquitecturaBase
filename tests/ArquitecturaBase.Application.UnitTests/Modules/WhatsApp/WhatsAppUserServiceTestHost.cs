using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

/// <summary>
/// El host de los servicios de usuarios con lo real del módulo WhatsApp, al lado de lo del núcleo: el canal telefónico
/// (con las opciones de siempre, solo AR), el canal de invitación y el participante del número, que suelta y vincula el
/// contacto del chat. <see cref="MessagesLog"/> anota los locks y las lecturas del repositorio de contactos.
/// </summary>
internal sealed class WhatsAppUserServiceTestHost : UserServiceTestHost
{
    public WhatsAppUserServiceTestHost()
        : this(new ModuleParts())
    {
    }

    private WhatsAppUserServiceTestHost(ModuleParts parts)
        : base(parts.PhoneChannel, [parts.InvitationChannel], [parts.Participant])
    {
        SendQueue = parts.SendQueue;
        MessagesLog = parts.MessagesLog;
        Contacts = parts.Contacts;
        MessagesLog.InTransaction = () => UnitOfWork.InTransaction;
    }

    public FakeWhatsAppSendQueue SendQueue { get; }

    public LockLog MessagesLog { get; }

    public InMemoryWhatsAppContactRepository Contacts { get; }

    // Lo del módulo se arma antes que el host: el constructor base ya lo necesita.
    private sealed class ModuleParts
    {
        public ModuleParts()
        {
            Contacts = new InMemoryWhatsAppContactRepository(MessagesLog);
            var phoneNumbers = new FakePhoneNumberParser();
            PhoneChannel = new WhatsAppPhoneChannel(
                new FakeWhatsAppAvailability(IsEnabled: true), Options.Create(new WhatsAppLoginOptions()), phoneNumbers);
            InvitationChannel = new WhatsAppInvitationChannel(
                SendQueue,
                new FakeWhatsAppAvailability(IsEnabled: true),
                new FakeAppName("Test"),
                NullLogger<WhatsAppInvitationChannel>.Instance);
            Participant = new WhatsAppPhoneLinkParticipant(new WhatsAppContactLinker(Contacts));
        }

        public LockLog MessagesLog { get; } = new();

        public FakeWhatsAppSendQueue SendQueue { get; } = new();

        public InMemoryWhatsAppContactRepository Contacts { get; }

        public WhatsAppPhoneChannel PhoneChannel { get; }

        public WhatsAppInvitationChannel InvitationChannel { get; }

        public WhatsAppPhoneLinkParticipant Participant { get; }
    }
}
