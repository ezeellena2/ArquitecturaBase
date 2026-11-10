using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
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

    public AssistantCapabilityRequest Request(string capability, string json = "{}", int version = 1) => new(TurnId, Context, new(capability, version), json, []);

    // A turn with the spreadsheet the engine prepared: the capability reads it from its request.
    public AssistantCapabilityRequest Request(string capability, string json, int version, StructuredSpreadsheet attachment) =>
        Request(capability, json, version) with { Attachment = attachment };

    // «Contratistas»: headers in row 1 and four rows of data in the sample, out of the 86 the file has.
    public static readonly string[][] ContractorRows =
    [
        ["CUIT", "Razón social", "Rubro", "Zona"],
        ["20-11111111-1", "Transportes Sur", "Cereales", "Norte"],
        ["20-22222222-2", "Logística Pampa", "", "Sur"],
        ["20-33333333-3", "Acopio Centro", "Hacienda", "Centro"],
        ["20-44444444-4", "Fletes del Oeste", "Cereales", "Oeste"],
    ];

    public static StructuredSpreadsheet Sheet(int rowCount = 87, string[][]? rows = null)
    {
        rows ??= ContractorRows;
        return new("contratistas.xlsx", StructuredSpreadsheetKind.Xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 2048,
            new string('a', 64), "Hoja1", rowCount, rows.Max(row => row.Length), rows.Select((cells, index) => new StructuredSpreadsheetRow(index + 1,
                cells.Select((text, column) => (Letter: ((char)('A' + column)).ToString(), Text: text)).Where(cell => cell.Text.Length > 0)
                    .ToDictionary(cell => cell.Letter, cell => cell.Text, StringComparer.Ordinal))).ToArray());
    }

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

        // The forms that exist, by their prefix.
        public Dictionary<string, FormAuthoringNamedRow> DefinitionsByPrefix { get; } = new(StringComparer.Ordinal);

        public Task<FormAuthoringNamedRow?> FindDefinitionByPrefixAsync(string prefix, CancellationToken cancellationToken) =>
            Task.FromResult(DefinitionsByPrefix.GetValueOrDefault(prefix));

        public Task<FormAuthoringCreatedRow?> FindCatalogByProposalAsync(Guid proposalId, CancellationToken cancellationToken) =>
            Task.FromResult(CreatedCatalogs.GetValueOrDefault(proposalId));

        public Task<bool> ExistsDefinitionNameAsync(string culture, string name, CancellationToken cancellationToken) =>
            Task.FromResult(NamesInUse.Contains(name));

        public Task<bool> ExistsCatalogNameAsync(string culture, string name, CancellationToken cancellationToken) =>
            Task.FromResult(NamesInUse.Contains(name));
    }
}
