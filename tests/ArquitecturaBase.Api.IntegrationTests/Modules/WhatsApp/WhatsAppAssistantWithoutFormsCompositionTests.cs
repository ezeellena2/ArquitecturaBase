using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// B5 with PostgreSQL: the composition of WhatsApp and IA, with the owners the host installs except those that declare `forms.*` (what is
// left without Formularios), builds the context of an automatic conversation that the engine accepts: every capability it asks for is
// declared by an installed owner, the rule the engine's catalog applies (a test of a module cannot name the catalog,
// ModuleBoundaryTests). With every owner installed, the same conversation still asks for what Formularios declares.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantWithoutFormsCompositionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_the_forms_capabilities_the_automatic_conversation_asks_only_for_what_is_installed()
    {
        var contactId = await AutomaticContactAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var contact = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().FindAsync([contactId], Ct);
        Assert.NotNull(contact);
        var all = services.GetServices<IAssistantCapabilitySource>().ToArray();
        IAssistantCapabilitySource[] withoutForms = [.. all.Where(source => !source.Definitions.Any(IsForms))];

        var captured = await ActivatorUtilities.CreateInstance<WhatsAppAssistantAccessPolicy>(services,
            new WhatsAppAssistantCapabilityPolicy(services.GetRequiredService<IWhatsAppAssistantAgentReader>(), withoutForms)).CaptureAsync(contact, null, Ct);
        var complete = await services.GetRequiredService<WhatsAppAssistantAccessPolicy>().CaptureAsync(contact, null, Ct);

        Assert.True(captured.IsSuccess);
        var context = captured.Value.Context;
        Assert.NotEmpty(context.AllowedCapabilities);
        Assert.All(Requested(context), reference => Assert.True(Declares(withoutForms, reference), $"{reference.Id} v{reference.Version} is not installed."));
        Assert.DoesNotContain(Requested(context), reference => reference.Id.StartsWith("forms.", StringComparison.Ordinal));
        Assert.True(complete.IsSuccess);
        Assert.All(Requested(complete.Value.Context), reference => Assert.True(Declares(all, reference)));
        if (all.Length != withoutForms.Length)
            Assert.Contains(Requested(complete.Value.Context), reference => reference.Id.StartsWith("forms.", StringComparison.Ordinal));
    }

    private static bool IsForms(AssistantCapabilityDefinition definition) => definition.Id.StartsWith("forms.", StringComparison.Ordinal);

    private static IEnumerable<AssistantCapabilityReference> Requested(AssistantChannelContext context) =>
        context.AllowedCapabilities.Concat(context.PreloadedCapabilities ?? []);

    private static bool Declares(IEnumerable<IAssistantCapabilitySource> sources, AssistantCapabilityReference reference) =>
        sources.SelectMany(source => source.Definitions).Any(definition => definition.Id == reference.Id && definition.Version == reference.Version);

    // A person with an active account, a confirmed number and Spanish, whose chat is in Asistente.
    private async Task<Guid> AutomaticContactAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var contactId = Guid.Empty;
        var result = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            var phone = PhoneNumber.Create("+" + MetaWebhook.UniqueWaId()).Value;
            var user = await services.GetRequiredService<IUserRepository>().CreateAsync(null, phone, true, "Persona sin formularios", "es", token);
            var contact = WhatsAppContact.Create(phone.Value[1..], MetaWebhook.UniqueBsuid(), "Persona ficticia", factory.Clock.GetUtcNow().UtcDateTime);
            contact.LinkUser(user.Id);
            contact.ChangeMode(WhatsAppConversationMode.Assistant, null, DateTime.UnixEpoch);
            services.GetRequiredService<ApplicationDbContext>().Add(contact);
            contactId = contact.Id;
            return Result.Success();
        }, CommitPolicy.OnSuccess, Ct);
        Assert.True(result.IsSuccess);
        return contactId;
    }
}
