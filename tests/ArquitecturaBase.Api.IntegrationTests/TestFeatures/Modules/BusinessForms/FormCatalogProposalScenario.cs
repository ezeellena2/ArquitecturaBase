using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed record FormCatalogProposalScenario(ApiFactory Factory, Guid RequesterId, Guid ReviewerId,
    Guid RoleId, string Suffix, string RequesterName, StructuredCatalogProposalAcceptanceRequest Request)
{
    public static async Task<FormCatalogProposalScenario> CreateAsync(ApiFactory factory, string culture, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var requesterName = "Requester " + suffix;
        var actors = await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            var roleName = "catalog-proposal-" + suffix;
            var roleId = await roles.CreateAsync(roleName, null, [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], ct);
            var users = services.GetRequiredService<IUserRepository>();
            var requester = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-proposer")).Value, null, false, requesterName, culture, ct);
            var reviewer = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-reviewer")).Value, null, false, "Reviewer " + suffix, culture, ct);
            await users.SetRolesAsync(requester.Id, [roleName], ct);
            await users.SetRolesAsync(reviewer.Id, [roleName], ct);
            return (requester.Id, ReviewerId: reviewer.Id, RoleId: roleId);
        });
        var occurred = await factory.ExecuteScopeAsync(services => Task.FromResult(services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime));
        var turn = Guid.NewGuid();
        var context = new AssistantChannelContext("whatsapp-assistant", actors.Id, actors.Id, culture, "America/Argentina/Buenos_Aires",
            new([], []), [new("forms.catalog-filters.metadata", 1), new("forms.catalog-filters.propose", 1)], [new("assistant_proposal", 1)]);
        var metadata = new FormCatalogMetadataPayload("users", [new("name", "text", true, true, true, ["contains", "equals"], null),
            new("role", "text", true, true, true, ["has"], [new(actors.RoleId.ToString(), "Fictitious reviewers")]),
            new("active", "boolean", true, true, true, ["equals"], [new("true", "Active"), new("false", "Inactive")])]);
        var payload = new FormCatalogProposalPayload(1, (culture == "en" ? "Proposed " : "Propuesto ") + suffix,
            culture == "en" ? "Fictitious active people" : "Personas activas ficticias",
            new("name", "active", [new("name", "equals", requesterName), new("active", "equals", "true")], false, "name", false),
            [new("UnsupportedColumn", culture == "en" ? "north zone" : "zona norte")]);
        var proposal = new AssistantObservedProposal(turn, Guid.NewGuid(), new("forms.catalog-filters.propose", 1), payload)
        { ObservedReads = [new(turn, 1, new("forms.catalog-filters.metadata", 1), metadata)] };
        return new(factory, actors.Id, actors.ReviewerId, actors.RoleId, suffix, requesterName,
            new(context, proposal, new("fictitious-profile", 3, "fictitious-instructions", 7, occurred, context.ChannelId)));
    }

    public HttpClient Client(Guid actor)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
        return client;
    }

    public async Task<StructuredCatalogProposalAcceptedResponse> AcceptAsync(CancellationToken ct)
    {
        var result = await Factory.ExecuteScopeAsync(services => services.GetRequiredService<IStructuredCatalogProposalSink>().AcceptAsync(Request, ct));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }
}
