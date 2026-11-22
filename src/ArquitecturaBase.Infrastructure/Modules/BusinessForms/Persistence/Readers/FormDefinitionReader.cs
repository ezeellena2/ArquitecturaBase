using System.Linq.Expressions;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed class FormDefinitionReader(ApplicationDbContext dbContext) : IFormDefinitionReader
{
    public Task<FormDefinitionStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        WithAudit(dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(definition => definition.Id == id))
            .Select(row => new FormDefinitionStoredRow(row.Definition.Id, row.Definition.NameJson, row.Definition.Prefix,
                row.Definition.PrefixLocked, row.Definition.Revision, row.Definition.Culture, row.Definition.SubmitLabelJson,
                row.Definition.SchemaJson, row.Definition.CreatedAtUtc, row.Definition.ModifiedAtUtc, row.LastChangedAtUtc,
                row.LastChangedByUserId == null ? null : new FormAuthorResponse(row.LastChangedByUserId.Value,
                    dbContext.Users.Where(user => user.Id == row.LastChangedByUserId).Select(user => user.DisplayName).FirstOrDefault()),
                row.Definition.OfferWhen, row.Definition.AppOnly) { StandardKey = row.Definition.StandardKey, StandardVersion = row.Definition.StandardVersion,
                    StandardBindingsJson = row.Definition.StandardBindingsJson, DocumentReadingJson = row.Definition.DocumentReadingJson }).SingleOrDefaultAsync(cancellationToken);
    public async Task<FormPublicationOverviewResponse> FindPublicationAsync(Guid id, Guid? recoveryActor, CancellationToken cancellationToken)
    {
        var activeRow = await (from definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(value => value.Id == id)
                               join version in dbContext.Set<BusinessFormVersion>() on definition.ActiveVersionId equals version.Id
                               join operation in dbContext.Set<BusinessFormPublicationOperation>() on version.Id equals operation.VersionId
                               where version.DefinitionId == id && operation.DefinitionId == id
                               select new { VersionId = version.Id, version.Number, operation.DraftRevision, OperationId = operation.Id,
                                   version.AppOnly, version.PublishedCultures, ReadsDocuments = version.DocumentReadingJson != null }).SingleOrDefaultAsync(cancellationToken);
        FormActiveVersionResponse? active = null;
        if (activeRow is not null)
        {
            // The languages are the ones the version sealed: reading them does not open the protected manifest (F27).
            active = new(activeRow.VersionId, activeRow.Number, activeRow.DraftRevision, activeRow.PublishedCultures ?? [],
                activeRow.AppOnly ? true : null, activeRow.ReadsDocuments ? true : null);
        }
        var latest = await dbContext.Set<BusinessFormPublicationOperation>().AsNoTracking().Where(value => value.DefinitionId == id)
            .OrderByDescending(value => value.VersionNumber)
            .Select(value => new FormCurrentPublicationResponse(value.VersionId, value.VersionNumber, value.DraftRevision,
                value.State.ToString(), value.Stage, value.State == FormPublicationState.Uncertain, false,
                // Whoever can publish learns an open operation to discard it; a closed one only its own operator.
                recoveryActor != null && (value.OperatorUserId == recoveryActor
                    || value.State != FormPublicationState.Published && value.State != FormPublicationState.Failed) ? (Guid?)value.RequestId : null)).FirstOrDefaultAsync(cancellationToken);
        return new(active, latest, latest?.State ?? (active is null ? "Draft" : "Published"));
    }
    public Task<FormChangeStoredRow?> FindChangeAsync(Guid actor, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormDraftChange>().AsNoTracking().Where(x => x.OperatorUserId == actor && x.RequestId == requestId)
            .Select(x => new FormChangeStoredRow(new(x.Id, x.OperatorUserId, x.RequestId, x.DefinitionId, x.Kind, x.ResultFormat),
                x.ProtectedResult, x.ResultExpiresAtUtc, x.CreatedAtUtc,
                dbContext.Set<BusinessFormDefinition>().IgnoreQueryFilters(new[] { ModelBuilderExtensions.SoftDeleteFilter })
                    .Where(definition => definition.Id == x.DefinitionId).Select(definition => definition.Revision).Single())).SingleOrDefaultAsync(cancellationToken);
    public Task<PagedResult<FormDefinitionListStoredRow>> ListAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken) =>
        ListQuery(request).ToPagedResultAsync(request, cancellationToken);

    private IQueryable<FormDefinitionListStoredRow> ListQuery(ListFormDefinitionsRequest request)
    {
        var nameColumn = request.DisplayCulture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        // Every column with a datum sorts by what the row shows, with the id of its column in the front (OT1-C); the subqueries
        // are those of the projection, in the same statement. «Deprecación» does not: it comes from the module of the channel.
        var sortable = new Dictionary<string, Expression<Func<DefinitionAuditRow, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = row => EF.Property<string>(row.Definition, nameColumn),
            ["questionCount"] = row => row.Definition.QuestionCount,
            // «Estado» in the order of its circuit, as the list names it: Borrador, Publicando (every open step), Publicación sin
            // confirmar, Publicado and No se pudo publicar. The latest publication is read once, as in DisplayState.
            ["displayState"] = row => dbContext.Set<BusinessFormPublicationOperation>().Where(operation => operation.DefinitionId == row.Definition.Id)
                .OrderByDescending(operation => operation.VersionNumber)
                .Select(operation => (int?)(operation.State == FormPublicationState.Uncertain ? 2 : operation.State == FormPublicationState.Published ? 3
                    : operation.State == FormPublicationState.Failed ? 4 : 1)).FirstOrDefault() ?? (row.Definition.ActiveVersionId == null ? 0 : 3),
            // «Dónde se completa», in the order the editor offers it: in the app and by WhatsApp, then only in the app.
            ["completedIn"] = row => dbContext.Set<BusinessFormVersion>().Where(version => version.Id == row.Definition.ActiveVersionId)
                .Select(version => (bool?)version.AppOnly).SingleOrDefault() ?? row.Definition.AppOnly,
            // A form never published has no active version («—»): it goes last in both directions.
            ["activeVersion"] = row => dbContext.Set<BusinessFormVersion>().Where(version => version.Id == row.Definition.ActiveVersionId)
                .Select(version => (long?)version.Number).SingleOrDefault(),
            // The responses, as the lifecycle counts them; Application keeps the usual order for whoever cannot read them.
            ["responseCount"] = row => row.Definition.LastRecordNumber,
            ["lastChangedAtUtc"] = row => row.LastChangedAtUtc,
            // Who changed it last, by the name the row shows; without one («—») it goes last.
            ["lastChangedBy"] = row => dbContext.Users.Where(user => user.Id == row.LastChangedByUserId).Select(user => user.DisplayName).FirstOrDefault(),
        };
        var definitions = Search(request.Search, request.DisplayCulture).InFolder(dbContext, request.FolderId, request.WithoutFolder);
        if (request.PublicationFilter == "Deactivated") definitions = definitions.Where(definition => definition.DeactivatedAtUtc != null);
        else definitions = definitions.Where(definition => definition.DeactivatedAtUtc == null);
        if (request.PublicationFilter == "Published") definitions = definitions.Where(definition => definition.ActiveVersionId != null);
        else if (request.PublicationFilter == "Drafts") definitions = definitions.Where(definition => definition.ActiveVersionId == null);
        return WithAudit(definitions).ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("name", false), row => row.Definition.Id)
            .Select(row => new FormDefinitionListStoredRow(row.Definition.Id, ReadName(row.Definition.NameJson), row.Definition.Prefix,
                row.Definition.QuestionCount,
                dbContext.Set<BusinessFormVersion>().Where(version => version.Id == row.Definition.ActiveVersionId).Select(version => (long?)version.Number).SingleOrDefault(),
                DisplayState(dbContext.Set<BusinessFormPublicationOperation>().Where(operation => operation.DefinitionId == row.Definition.Id)
                    .OrderByDescending(operation => operation.VersionNumber).Select(operation => (FormPublicationState?)operation.State).FirstOrDefault(),
                    row.Definition.ActiveVersionId != null),
                row.LastChangedAtUtc, row.LastChangedByUserId == null ? null : new FormAuthorResponse(row.LastChangedByUserId.Value,
                    dbContext.Users.Where(user => user.Id == row.LastChangedByUserId).Select(user => user.DisplayName).FirstOrDefault()),
                // Where the active version is completed; a form never published, where its draft will be (SA3-A).
                dbContext.Set<BusinessFormVersion>().Where(version => version.Id == row.Definition.ActiveVersionId)
                    .Select(version => (bool?)version.AppOnly).SingleOrDefault() ?? row.Definition.AppOnly));
    }

    public async Task<FormDefinitionCounts> CountFacetsAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken)
    {
        var counts = await Search(request.Search, request.DisplayCulture).InFolder(dbContext, request.FolderId, request.WithoutFolder).GroupBy(_ => 1).Select(group => new FormDefinitionCounts(
            group.Count(value => value.DeactivatedAtUtc == null),
            group.Count(value => value.DeactivatedAtUtc == null && value.ActiveVersionId != null),
            group.Count(value => value.DeactivatedAtUtc == null && value.ActiveVersionId == null),
            group.Count(value => value.DeactivatedAtUtc != null))).SingleOrDefaultAsync(cancellationToken);
        return counts ?? new(0, 0, 0, 0);
    }

    private IQueryable<DefinitionAuditRow> WithAudit(IQueryable<BusinessFormDefinition> definitions) =>
        from definition in definitions
        let draft = dbContext.Set<BusinessFormDraftChange>()
            .Where(change => change.DefinitionId == definition.Id && change.ConfirmedRevision <= definition.Revision)
            .OrderByDescending(change => change.ConfirmedRevision).ThenByDescending(change => change.Id)
            .Select(change => new { change.ConfirmedRevision, change.CreatedAtUtc, change.OperatorUserId }).FirstOrDefault()
        let draftChangedAtUtc = draft == null ? definition.CreatedAtUtc : draft.CreatedAtUtc
        let draftChangedBy = draft == null ? definition.CreatedBy : (Guid?)draft.OperatorUserId
        let deactivationIsLatest = definition.DeactivatedAtUtc != null && (draft == null || draft.ConfirmedRevision < definition.Revision)
        select new DefinitionAuditRow
        {
            Definition = definition,
            LastChangedAtUtc = deactivationIsLatest ? definition.DeactivatedAtUtc!.Value : draftChangedAtUtc,
            LastChangedByUserId = deactivationIsLatest ? definition.DeactivatedBy : draftChangedBy,
        };

    private sealed class DefinitionAuditRow
    {
        public BusinessFormDefinition Definition { get; init; } = null!;
        public DateTime LastChangedAtUtc { get; init; }
        public Guid? LastChangedByUserId { get; init; }
    }

    private static Dictionary<string, string> ReadName(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("The stored form name is invalid.");
    private static string DisplayState(FormPublicationState? latestState, bool hasActiveVersion) => latestState?.ToString() ?? (hasActiveVersion ? "Published" : "Draft");
    private IQueryable<BusinessFormDefinition> Search(string? search, string displayCulture)
    {
        var query = dbContext.Set<BusinessFormDefinition>().AsNoTracking();
        if (string.IsNullOrWhiteSpace(search)) return query;
        var nameColumn = displayCulture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        var pattern = LikePatterns.Contains(search.Trim());
        return query.Where(x => EF.Functions.ILike(EF.Property<string>(x, nameColumn), pattern, LikePatterns.EscapeCharacter) || EF.Functions.ILike(x.Prefix, pattern, LikePatterns.EscapeCharacter));
    }
}