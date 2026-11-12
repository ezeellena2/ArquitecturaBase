using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Documents (task 3) with PostgreSQL: what the automatic assistant offers, from the list its access policy captures and the definitions
// the installed owners declare through the neutral port, by the rule each definition states for the turn's file. With every permission:
// 15 definitions without a file, 16 with a spreadsheet (the cap) and 11 with a document, which leaves out the import, the three text
// registrations and the missing form, all of the text circuit. The two capabilities of documents come later and take two of the free
// places. A test of a module cannot name the engine's catalog (ModuleBoundaryTests): the rule is the one the catalog applies.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantFileCatalogTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_automatic_assistant_offers_fifteen_definitions_without_a_file_sixteen_with_a_spreadsheet_and_eleven_with_a_document()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct);
        var person = await host.ContactAsync(Ct, automatic: true);
        var (context, definitions) = await host.ScopeAsync(async services =>
        {
            var contact = await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(row => row.Id == person.ContactId, Ct);
            var captured = await services.GetRequiredService<WhatsAppAssistantAccessPolicy>().CaptureAsync(contact, null, Ct);
            Assert.True(captured.IsSuccess);
            var declared = services.GetServices<IAssistantCapabilitySource>().SelectMany(source => source.Definitions)
                .ToDictionary(definition => new AssistantCapabilityReference(definition.Id, definition.Version));
            return (captured.Value.Context, declared);
        });
        int Offered(AssistantAttachmentKind file) =>
            context.AllowedCapabilities.Count(reference => definitions[reference].OfferedWith(file)) + context.AllowedTerminals.Count;

        Assert.Equal((15, 16, 11), (Offered(AssistantAttachmentKind.None), Offered(AssistantAttachmentKind.Spreadsheet), Offered(AssistantAttachmentKind.Document)));
        Assert.Equal(["forms.assistant-candidates", "forms.authoring.propose-import", "forms.clarify-registration", "forms.missing-request.propose",
                "forms.propose-registration"],
            context.AllowedCapabilities.Where(reference => !definitions[reference].OfferedWith(AssistantAttachmentKind.Document))
                .Select(reference => reference.Id).Order(StringComparer.Ordinal));
    }
}
