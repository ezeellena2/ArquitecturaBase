using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class AuthoringAssistantFixture
{
    public static readonly string[] AllPermissions = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
        BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
    public Guid TurnId { get; } = Guid.NewGuid();
    public InMemoryUserAccounts Users { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public AuthoringReader Reader { get; } = new();
    public Guid Actor { get; }
    public BusinessFormAuthoringAssistantCapabilitySource Source { get; }

    public AuthoringAssistantFixture(string[]? permissions = null, bool active = true)
    {
        Actor = Users.AddUser("authoring@example.test", isActive: active).Id;
        Permissions.Permissions[Actor] = permissions ?? AllPermissions;
        Source = new(Reader, Users, Permissions);
    }

    public AssistantChannelContext Context => new("home", Actor, null, "es", "America/Argentina/Buenos_Aires", new([], []),
        Source.Definitions.Select(definition => new AssistantCapabilityReference(definition.Id, definition.Version)).ToArray(),
        [new("assistant_answer", 1)]);

    public AssistantCapabilityRequest Request(string capability, string json = "{}") => new(TurnId, Context, new(capability, 1), json, []);

    public static FormAuthoringDefinitionStoredRow Definition(string name, string prefix, bool published = false, bool inactive = false,
        params (string Label, string Type)[] questions) =>
        new(FormDraftPolicy.Serialize(new Dictionary<string, string> { ["es"] = name }), "es", prefix, published, inactive,
            FormDraftPolicy.Serialize(new FormQuestionSchema(1, questions.Select((question, index) => new CanonicalFormField(Guid.NewGuid(),
                "q" + index, question.Type, new Dictionary<string, string> { ["es"] = question.Label }, null, false, null, null)).ToArray())));

    public static FormAuthoringCatalogStoredRow Catalog(Guid id, string name, BusinessFormCatalogKind kind = BusinessFormCatalogKind.Manual,
        bool active = true, params BusinessFormCatalogField[] fields) =>
        new(id, FormDraftPolicy.Serialize(new Dictionary<string, string> { ["es"] = name }), "es", kind, BusinessFormCatalogValueType.Text,
            active, fields.Length == 0 ? null : FormDraftPolicy.Serialize(fields));

    public static BusinessFormCatalogField Field(string key, string name, string type = "text", bool active = true, int position = 0) =>
        new(Guid.NewGuid(), key, new Dictionary<string, string> { ["es"] = name }, type, null, false, false, null, null, null, "data", position, active);

    public sealed class AuthoringReader : IFormAuthoringReader
    {
        public List<FormAuthoringDefinitionStoredRow> Definitions { get; } = [];
        public List<FormAuthoringCatalogStoredRow> Catalogs { get; } = [];
        public int Reads { get; private set; }
        public int LastTake { get; private set; }

        public Task<IReadOnlyList<FormAuthoringDefinitionStoredRow>> ListDefinitionsAsync(int take, CancellationToken cancellationToken)
        {
            Reads++;
            LastTake = take;
            return Task.FromResult<IReadOnlyList<FormAuthoringDefinitionStoredRow>>(Definitions.Take(take).ToArray());
        }

        public Task<IReadOnlyList<FormAuthoringCatalogStoredRow>> ListCatalogsAsync(int take, CancellationToken cancellationToken)
        {
            Reads++;
            LastTake = take;
            return Task.FromResult<IReadOnlyList<FormAuthoringCatalogStoredRow>>(Catalogs.Take(take).ToArray());
        }

        public Task<IReadOnlyList<FormAuthoringCatalogStoredRow>> ListCatalogsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<FormAuthoringCatalogStoredRow>>(Catalogs.Where(catalog => ids.Contains(catalog.Id)).ToArray());
        }

        // What each proposal already created, by its proposal, and the names in use in any language.
        public Dictionary<Guid, FormAuthoringCreatedRow> CreatedDefinitions { get; } = [];
        public Dictionary<Guid, FormAuthoringCreatedRow> CreatedCatalogs { get; } = [];
        public HashSet<string> NamesInUse { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<FormAuthoringCreatedRow?> FindDefinitionByProposalAsync(Guid proposalId, CancellationToken cancellationToken) =>
            Task.FromResult(CreatedDefinitions.GetValueOrDefault(proposalId));

        public Task<FormAuthoringCreatedRow?> FindCatalogByProposalAsync(Guid proposalId, CancellationToken cancellationToken) =>
            Task.FromResult(CreatedCatalogs.GetValueOrDefault(proposalId));

        public Task<bool> ExistsDefinitionNameAsync(string culture, string name, CancellationToken cancellationToken) =>
            Task.FromResult(NamesInUse.Contains(name));

        public Task<bool> ExistsCatalogNameAsync(string culture, string name, CancellationToken cancellationToken) =>
            Task.FromResult(NamesInUse.Contains(name));
    }
}
