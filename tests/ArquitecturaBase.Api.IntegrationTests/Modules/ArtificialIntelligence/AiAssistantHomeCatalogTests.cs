using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// What Home offers with the modules the host composes, for a person with every permission: the five queries, what the owners
// mark OfferedInHome and the terminal. Leaving out what the turn's file does not admit happens before counting the sixteen definitions.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantHomeCatalogTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // IA, Stage 2: the catalog maps the spreadsheet (version 2, never version 1 next to it) and the import needs one, so Home
    // has twelve definitions without a spreadsheet, thirteen with it and twelve with a document.
    [Fact]
    public async Task Home_offers_the_mapped_catalog_always_and_the_import_only_with_a_spreadsheet()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional forms module must be installed for this composition test.");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var personId = await factory.InTransactionAsync(async services =>
        {
            var role = "home-catalog-" + suffix;
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, services.GetRequiredService<IPermissionCatalog>().All.ToArray(), Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var account = await users.CreateAsync(Email.Create(TestEmails.Unique("home-catalog")).Value, null, false, "Home " + suffix, "es", Ct);
            await users.SetRolesAsync(account.Id, [role], Ct);
            return account.Id;
        });

        await factory.ExecuteScopeAsync(async services =>
        {
            var catalog = services.GetRequiredService<AiAssistantCapabilityCatalogPolicy>();
            var home = AiAssistantChannelPolicy.Home(personId, "es", "America/Argentina/Buenos_Aires", catalog.OfferedInHome);
            var without = await catalog.BuildAsync(home, AssistantAttachmentKind.None, Ct);
            var with = await catalog.BuildAsync(home, AssistantAttachmentKind.Spreadsheet, Ct);
            var document = await catalog.BuildAsync(home, AssistantAttachmentKind.Document, Ct);
            Assert.True(without.IsSuccess);
            Assert.True(with.IsSuccess);
            Assert.True(document.IsSuccess);
            Assert.Equal(12, without.Value.Capabilities.Count + without.Value.Terminals.Count);
            Assert.Equal(13, with.Value.Capabilities.Count + with.Value.Terminals.Count);
            // A document leaves out the import, which needs a spreadsheet; Home offers none of the text registrations.
            Assert.Equal(12, document.Value.Capabilities.Count + document.Value.Terminals.Count);
            var offered = with.Value.Capabilities.Select(definition => new AssistantCapabilityReference(definition.Id, definition.Version)).ToArray();
            Assert.Contains(new AssistantCapabilityReference("forms.authoring.propose-catalog", 2), offered);
            Assert.DoesNotContain(new AssistantCapabilityReference("forms.authoring.propose-catalog", 1), offered);
            Assert.Contains(new AssistantCapabilityReference("forms.authoring.propose-import", 1), offered);
            Assert.Equal(offered.Except([new AssistantCapabilityReference("forms.authoring.propose-import", 1)]),
                without.Value.Capabilities.Select(definition => new AssistantCapabilityReference(definition.Id, definition.Version)));
            Assert.Equal(without.Value.Capabilities, document.Value.Capabilities);
            return 0;
        });
    }
}
