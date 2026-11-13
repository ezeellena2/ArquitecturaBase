using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AssistantQualification;

// Every form QualificationFormSeed publishes, the reading forms of channel-documents included, passes the real check of the phone channel
// with the request the seed builds, so a fixture the compiler refuses never stops a paid run while seeding. The channel comes from the
// app's own registration; it never reaches a WhatsApp account, Meta or a database.
internal static class FormSeedOfflineChecks
{
    public static readonly Action[] All = [Seeded_forms_pass_the_real_phone_channel_check];

    // CheckAsync only asks whether a WhatsApp account is configured; publishing or polling never happens here.
    private sealed class NoFlowAccount : IWhatsAppFlowEnvironment, IWhatsAppFlowPublicationService, IWhatsAppFlowStatusService
    {
        public bool IsConfigured => false;
        public TimeSpan SessionLifetime => throw Unexpected();
        public Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken cancellationToken) => throw Unexpected();
        public Task<Result<StructuredFormPublicationResponse>> AcceptAsync(StructuredFormPublicationRequest request, CancellationToken cancellationToken) => throw Unexpected();
        public Task<Result<StructuredFormPublicationResponse>> GetAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken) => throw Unexpected();
        public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken) => throw Unexpected();
        private static InvalidOperationException Unexpected() => new("Offline check reached a WhatsApp account.");
    }

    private static void Seeded_forms_pass_the_real_phone_channel_check()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddWhatsAppApplication();
        var none = new NoFlowAccount();
        services.AddSingleton<IWhatsAppFlowEnvironment>(none).AddSingleton<IWhatsAppFlowPublicationService>(none).AddSingleton<IWhatsAppFlowStatusService>(none);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var phone = scope.ServiceProvider.GetServices<IStructuredFormChannel>().Single(channel => channel.ChannelId == "phone");
        var requests = QualificationFormSeed.OfflineCheckRequests();
        Require(requests.Length == QualificationFormSeed.FormNames.Length + QualificationChannelDocuments.ReadingForms.Length
            && requests.Count(request => request.ReadsDocuments == true) == QualificationChannelDocuments.ReadingForms.Length);
        foreach (var request in requests)
        {
            var check = phone.CheckAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            Require(check.IsSuccess && check.Value.Issues.Count == 0 && check.Value.Cultures.Count == request.TargetCultures.Count);
        }
    }

    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Offline check failed."); }
}
