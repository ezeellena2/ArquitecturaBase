using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// A host of its own whose only difference is the proposal port: the test hands out a Home proposal as a completed turn would.
internal sealed class FormAssistantProposalScenario(ApiFactory factory, WebApplicationFactory<Program> host, ScriptedAssistantProposalLookup lookup,
    Guid authorId, Guid readerId, Guid outsiderId, string suffix, DateTime occurredAtUtc) : IAsyncDisposable
{
    public ScriptedAssistantProposalLookup Lookup => lookup;
    public Guid AuthorId => authorId;
    public Guid ReaderId => readerId;
    public Guid OutsiderId => outsiderId;
    public string Suffix => suffix;
    public string Prefix => "IA" + suffix[..6].ToUpperInvariant();

    public static async Task<FormAssistantProposalScenario> CreateAsync(ApiFactory factory, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var people = await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            await roles.CreateAsync("assistant-author-" + suffix, null, [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
                BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], ct);
            await roles.CreateAsync("assistant-reader-" + suffix, null, [BusinessFormsPermissions.Read, BusinessFormsPermissions.CatalogsRead], ct);
            var users = services.GetRequiredService<IUserRepository>();
            var author = await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-author")).Value, null, false, "Author " + suffix, "es", ct);
            var reader = await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-reader")).Value, null, false, "Reader " + suffix, "es", ct);
            var outsider = await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-outsider")).Value, null, false, "Outsider " + suffix, "es", ct);
            await users.SetRolesAsync(author.Id, ["assistant-author-" + suffix], ct);
            await users.SetRolesAsync(reader.Id, ["assistant-reader-" + suffix], ct);
            return (Author: author.Id, Reader: reader.Id, Outsider: outsider.Id);
        });
        var occurred = await factory.ExecuteScopeAsync(services => Task.FromResult(services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime));
        var lookup = new ScriptedAssistantProposalLookup();
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IAssistantProposalLookup>(lookup))));
        return new(factory, host, lookup, people.Author, people.Reader, people.Outsider, suffix, occurred);
    }

    public HttpClient Client(Guid actor)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
        return client;
    }

    public HttpClient AnonymousClient() => host.CreateClient();

    public CreateFromAssistantProposalRequest Form(string name, string prefix, bool current = true) =>
        Hand(new(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, 1), new FormAuthoringFormProposal("es", name, prefix, "Enviar",
            [new("Patente", "shortText", true, [], null), new("Kilos", "number", false, [], null)]), current);

    public CreateFromAssistantProposalRequest Catalog(string name, bool current = true) =>
        Hand(new(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, 1), new FormAuthoringCatalogProposal("es", name,
            "Empresas de transporte ficticias", "text", "CUIT", "Razón social", [new("Razón social", "text"), new("Teléfono", "text")]), current);

    public Task<BusinessFormDefinition> DefinitionAsync(Guid id, CancellationToken ct) => factory.ExecuteScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormDefinition>().AsNoTracking().SingleAsync(definition => definition.Id == id, ct));

    public Task<int> CountDefinitionsAsync(Guid proposalId, CancellationToken ct) => factory.ExecuteScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormDefinition>().CountAsync(definition => definition.ProposalId == proposalId, ct));

    public ValueTask DisposeAsync() => host.DisposeAsync();

    private CreateFromAssistantProposalRequest Hand(AssistantCapabilityReference capability, AssistantCapabilityPayload payload, bool current)
    {
        var requestId = Guid.NewGuid();
        var context = new AssistantChannelContext("home", authorId, null, "es", "America/Argentina/Buenos_Aires", new([], []), [capability],
            [new("assistant_answer", 2)]);
        var proposal = new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), capability, payload);
        lookup.Add(authorId, requestId, new(context, proposal, new("fictitious-profile", 3, "home-system", 2, occurredAtUtc, "home"), current));
        return new(requestId, proposal.Id);
    }
}
