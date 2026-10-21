using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class CatalogAssistantFixture
{
    public static readonly Guid RoleId = new("3c8767d3-78cb-4782-9d42-462d73b898bc");
    public Guid TurnId { get; } = Guid.NewGuid();
    public InMemoryUserAccounts Users { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public MetadataReader Reader { get; } = new();
    public Guid Actor { get; }
    public BusinessFormCatalogAssistantCapabilitySource Source { get; }
    public CatalogAssistantFixture(string[]? permissions = null, bool active = true)
    {
        Actor = Users.AddUser("catalog-proposal@example.test", isActive: active).Id;
        Permissions.Permissions[Actor] = permissions ?? [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        Source = new(Reader, Users, Permissions);
    }
    public AssistantChannelContext Context => new("whatsapp-assistant", Actor, Actor, "es", "America/Argentina/Buenos_Aires", new([], []),
        [new(BusinessFormCatalogAssistantCapabilitySource.MetadataId, 1), new(BusinessFormCatalogAssistantCapabilitySource.ProposeId, 1)], [new("assistant_proposal", 1)]) { InteractionMode = AssistantInteractionMode.Assistant };
    public AssistantCapabilityRequest Request(string capability, string json = "{}", IReadOnlyList<AssistantObservedRead>? reads = null) =>
        new(TurnId, Context, new(capability, 1), json, reads ?? []);
    public static FormCatalogMetadataPayload Metadata => new("users", [new("name", "text", true, true, true, ["contains", "equals"], null),
        new("role", "text", true, true, true, ["has"], [new(RoleId.ToString(), "Supervisores")]),
        new("active", "boolean", true, true, true, ["equals"], [new("true", "Activo"), new("false", "Inactivo")])]);
    public static FormCatalogProposalPayload Proposal => new(2, "Supervisores", "Supervisores activos", new("name", "role", [new("role", "has", RoleId.ToString()), new("active", "equals", "true")], false, "name", false), []);
    public AssistantObservedRead Observed(int ordinal = 2) => new(TurnId, ordinal, new(BusinessFormCatalogAssistantCapabilitySource.MetadataId, 1), Metadata);
    public sealed class MetadataReader : IFormCatalogFilterReader
    {
        public int Reads { get; private set; }
        public IReadOnlyList<FormCatalogFilterChoiceResponse> Roles { get; set; } = [new(RoleId.ToString(), "Supervisores")];
        public Task<IReadOnlyList<FormCatalogFilterChoiceResponse>> ListRolesAsync(CancellationToken ct) { Reads++; return Task.FromResult(Roles); }
        public Task<PagedResult<FormCatalogFilterSampleUserResponse>> ListSampleUsersAsync(FormCatalogFilterOptionsRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsActiveSampleUserAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<FormCatalogFilterPreviewResponse> FindPreviewAsync(Guid userId, FormCatalogFilterDefinition filters, CancellationToken ct) => throw new NotSupportedException();
    }
}
