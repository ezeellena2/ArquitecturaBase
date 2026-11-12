using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

/// <summary>
/// The real entry service and catalog policies over in-memory reads: one published version with a catalog question
/// (<see cref="FormCatalogPreparationFixture"/>) and a capture double that records what reaches it.
/// </summary>
internal sealed class FormEntryFixture : IFormEntryReader, IBusinessFormRecordCaptureService, IDisposable
{
    private readonly ServiceProvider _provider;
    public FormCatalogPreparationFixture Catalogs { get; }
    public FormCatalogPersonReader People { get; } = new();
    public InMemoryUserAccounts Users { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public Guid Actor { get; }
    public Guid? ActiveVersionId { get; set; }
    public bool Deactivated { get; set; }
    public bool Missing { get; set; }
    /// <summary>Another sealed schema for the active version, such as one with section titles.</summary>
    public string? SchemaJson { get; set; }
    public List<FormEntryItemsQuery> ItemQueries { get; } = [];
    public List<StructuredRecordCaptureRequest> Captures { get; } = [];
    /// <summary>The responses a correction opens, by their stored rows.</summary>
    public FakeFormResponseReader Responses { get; } = new();
    public StructuredRecordCaptureResponse? Existing { get; set; }
    public StructuredRecordCaptureResponse Captured { get; set; } = new("Completed", Guid.NewGuid(), 42, null);
    public FormEntryOutcomeRow Outcome { get; set; } = new(new DateTime(2026, 10, 30, 13, 58, 0, DateTimeKind.Utc), "Received", false);

    public FormEntryFixture(bool filtered = false, bool required = true, string questionType = "choice", FormSelectionSettings? selection = null,
        bool dependsOnAnswer = false, string? groupBy = null, BusinessFormCatalogValueType valueType = BusinessFormCatalogValueType.Text)
    {
        Catalogs = new(filtered, required, valueType: valueType, questionType: questionType, selection: selection, dependsOnAnswer: dependsOnAnswer, groupByType: groupBy);
        ActiveVersionId = Catalogs.Version.Id;
        Actor = Users.AddUser("entry@example.invalid").Id;
        Current.UserId = Actor;
        Permissions.Permissions[Actor] = [BusinessFormsPermissions.RecordsCreate];
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddBusinessFormsApplication();
        services.AddSingleton<ICurrentUser>(Current);
        services.AddSingleton<IPermissionService>(Permissions);
        services.AddSingleton<IUserReader>(Users);
        services.AddSingleton<IFormEntryReader>(this);
        services.AddSingleton<IFormResponseReader>(Responses);
        services.AddSingleton<IFormRecordIssuanceReader>(new NoIssuances());
        services.AddSingleton<IFormCatalogReader>(Catalogs);
        services.AddSingleton<IFormCatalogFilterReader>(Catalogs);
        services.AddSingleton<IFormPersonDataReader>(People);
        services.AddSingleton<IFormRuleReader>(Catalogs.Rules);
        services.AddSingleton<IFormPdfDesignReader>(Catalogs);
        services.Replace(ServiceDescriptor.Singleton<IBusinessFormRecordCaptureService>(this));
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 13, 58, 0, TimeSpan.Zero)));
        services.AddScoped<IRequestValidator, RequestValidator>();
        _provider = services.BuildServiceProvider();
    }

    public IFormEntryService Service => _provider.GetRequiredService<IFormEntryService>();
    public Guid FieldId => Catalogs.FieldId;

    public BusinessFormCatalogItem AddItem(string text, int position, string? detail = null, BusinessFormCatalogItem? answer = null, string? value = null)
    {
        var item = BusinessFormCatalogItem.Create(Catalogs.Catalog.Id, "C" + position, text, null, detail, null, position, value);
        if (answer is not null && Catalogs.Catalog.SetManualItemFields(item,
            FormDraftPolicy.Serialize(new Dictionary<string, string> { [Catalogs.FilterFieldId.ToString("D")] = answer.Id.ToString("D") })).IsFailure)
            throw new InvalidOperationException("The option of the dependent list is invalid.");
        Catalogs.Items.Add(item);
        return item;
    }

    public SubmitFormEntryRequest Send(params FormEntryAnswer[] answers) => new(Guid.NewGuid(), Catalogs.Version.Id, answers, []);

    /// <summary>LD1-A: an option whose field to choose first holds <paramref name="group"/> (a text or a source option's id), or nothing.</summary>
    public BusinessFormCatalogItem AddGrouped(string text, int position, string? group)
    {
        var item = BusinessFormCatalogItem.Create(Catalogs.Catalog.Id, "G" + position, text, null, null, null, position);
        var values = group is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [Catalogs.GroupFieldId.ToString("D")] = group };
        if (Catalogs.Catalog.SetManualItemFields(item, FormDraftPolicy.Serialize(values)).IsFailure)
            throw new InvalidOperationException("The grouped option is invalid.");
        Catalogs.Items.Add(item);
        return item;
    }

    /// <summary>An option of the catalog the field to choose first takes its values from.</summary>
    public BusinessFormCatalogItem AddSource(string es, string en, int position)
    {
        var item = BusinessFormCatalogItem.Create(Catalogs.GroupSourceId, "S" + position, es, en, null, null, position);
        Catalogs.Items.Add(item);
        return item;
    }

    public List<(Guid CatalogId, string? Containment, Guid FieldId)> GroupQueries { get; } = [];

    public Task<IReadOnlyList<FormEntryGroupRow>> ListGroupsAsync(Guid catalogId, string? containment, Guid fieldId, CancellationToken ct)
    {
        GroupQueries.Add((catalogId, containment, fieldId));
        var matches = FormCatalogFieldPolicy.ReadValues(containment ?? "{}");
        var rows = Catalogs.Items.Where(item => item.CatalogId == catalogId && item.IsActive)
            .Where(item => { var values = FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson); return matches.All(match => values.TryGetValue(match.Key, out var value) && value.GetRawText() == match.Value.GetRawText()); })
            .GroupBy(item => GroupValue(item, fieldId.ToString("D")))
            .Select(group => new FormEntryGroupRow(group.Key, group.Count())).ToArray();
        return Task.FromResult<IReadOnlyList<FormEntryGroupRow>>(rows);
    }

    private static string? GroupValue(BusinessFormCatalogItem item, string key) =>
        FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson).TryGetValue(key, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            && value.GetString() is { Length: > 0 } text ? text : null;

    public Task<FormEntryDefinitionRow?> FindDefinitionAsync(Guid definitionId, Guid? versionId, CancellationToken ct)
    {
        if (Missing || definitionId != Catalogs.DefinitionId) return Task.FromResult<FormEntryDefinitionRow?>(null);
        var version = Catalogs.Version;
        var row = (versionId ?? ActiveVersionId) == version.Id
            ? new FormEntryVersionRow(version.Id, version.Number, version.NameJson!, version.SubmitLabelJson!, version.Culture!, SchemaJson ?? version.SchemaJson) : null;
        return Task.FromResult<FormEntryDefinitionRow?>(new(definitionId, "SIE", ActiveVersionId, Deactivated, row));
    }

    public Task<PagedResult<FormCatalogStoredItemRow>> ListItemsAsync(FormEntryItemsQuery query, CancellationToken ct)
    {
        ItemQueries.Add(query);
        var matches = FormCatalogFieldPolicy.ReadValues(query.Containment ?? "{}");
        var items = Catalogs.Items.Where(item => item.CatalogId == query.CatalogId && item.IsActive)
            .Where(item => { var values = FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson); return matches.All(match => values.TryGetValue(match.Key, out var value) && value.GetRawText() == match.Value.GetRawText()); })
            .Where(item => query.Ids is null || query.Ids.Contains(item.Id))
            .Where(item => query.WithoutField is null || GroupValue(item, query.WithoutField) is null)
            .Where(item => query.Search is null || new[] { item.Code, item.TextEs, item.TextEn, item.DetailEs, item.DetailEn }
                .Any(text => text?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(item => item.Position).ThenBy(item => item.Id).ToArray();
        return Task.FromResult(new PagedResult<FormCatalogStoredItemRow>(items.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(item => new FormCatalogStoredItemRow(item.Id, item.CatalogId, item.Code, item.TextEs, item.TextEn, item.DetailEs, item.DetailEn,
                item.Position, item.IsActive, item.Value, item.FieldValuesJson)).ToArray(), query.Page, query.PageSize, items.Length));
    }

    public Task<PagedResult<FormCatalogFilterOptionResponse>> ListPeopleAsync(Guid recipientUserId, FormCatalogFilterDefinition filters,
        FormEntryItemsQuery query, CancellationToken ct)
    {
        ItemQueries.Add(query);
        var people = Catalogs.Preview.Items.Where(item => query.Ids is null || query.Ids.Contains(item.Id))
            .Where(item => query.Search is null || item.Text.Contains(query.Search, StringComparison.OrdinalIgnoreCase)).ToArray();
        return Task.FromResult(new PagedResult<FormCatalogFilterOptionResponse>(people.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(),
            query.Page, query.PageSize, people.Length));
    }

    public Task<FormEntryOutcomeRow?> FindOutcomeAsync(Guid recordId, CancellationToken ct) => Task.FromResult<FormEntryOutcomeRow?>(Outcome);
    public Task<IReadOnlyList<FormEntryOriginRow>> ListOriginsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<FormEntryOriginRow>>([]);

    public Task<StructuredRecordCaptureResponse> CaptureEntryAsync(StructuredRecordCaptureRequest request, CancellationToken ct)
    {
        Captures.Add(request);
        return Task.FromResult(Captured);
    }
    public Task<StructuredRecordCaptureResponse?> FindExistingEntryAsync(StructuredRecordCaptureRequest request, CancellationToken ct) => Task.FromResult(Existing);
    public List<(StructuredRecordCaptureRequest Request, FormRecordCorrection Correction)> Corrections { get; } = [];
    public StructuredRecordCaptureResponse? ExistingCorrection { get; set; }
    public Task<StructuredRecordCaptureResponse> CaptureCorrectionAsync(StructuredRecordCaptureRequest request, FormRecordCorrection correction, CancellationToken ct)
    {
        Corrections.Add((request, correction));
        return Task.FromResult(Captured);
    }
    public Task<StructuredRecordCaptureResponse?> FindExistingCorrectionAsync(StructuredRecordCaptureRequest request, FormRecordCorrection correction,
        CancellationToken ct) => Task.FromResult(ExistingCorrection);
    public Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<StructuredRecordCaptureResponse?> FindExistingReceiptAsync(StructuredRecordCaptureRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<StructuredRecordCaptureResponse> CaptureDocumentAsync(StructuredRecordCaptureRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<StructuredRecordCaptureResponse?> FindExistingDocumentAsync(StructuredRecordCaptureRequest request, CancellationToken ct) => throw new NotSupportedException();

    public void Dispose() => _provider.Dispose();
}
