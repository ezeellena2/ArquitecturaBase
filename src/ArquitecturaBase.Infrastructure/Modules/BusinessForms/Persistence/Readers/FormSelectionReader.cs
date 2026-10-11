using System.Linq.Expressions;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormSelectionReader(ApplicationDbContext dbContext) : IFormSelectionReader
{
    public Task<StructuredFormSelectionRow?> FindAsync(Guid definitionId, Guid versionId, CancellationToken cancellationToken) =>
        Active().Where(version => version.DefinitionId == definitionId && version.Id == versionId)
            .Select(version => new StructuredFormSelectionRow(version.DefinitionId, version.Id, version.Number,
                dbContext.Set<BusinessFormDefinition>().Where(definition => definition.Id == version.DefinitionId).Select(definition => definition.Prefix).Single(),
                ReadTexts(version.NameJson!), version.QuestionCount!.Value, ReadTexts(version.SubmitLabelJson!)))
            .SingleOrDefaultAsync(cancellationToken);
    public Task<StructuredFormSelectionRow?> FindPublishedAsync(Guid definitionId, Guid versionId, CancellationToken cancellationToken) =>
        Published().Where(version => version.DefinitionId == definitionId && version.Id == versionId)
            .Select(version => new StructuredFormSelectionRow(version.DefinitionId, version.Id, version.Number,
                dbContext.Set<BusinessFormDefinition>().Where(definition => definition.Id == version.DefinitionId).Select(definition => definition.Prefix).Single(),
                ReadTexts(version.NameJson!), version.QuestionCount!.Value, ReadTexts(version.SubmitLabelJson!)))
            .SingleOrDefaultAsync(cancellationToken);
    public Task<PagedResult<StructuredFormSelectionRow>> ListAsync(string recipientCulture, ListStructuredFormsRequest request, CancellationToken cancellationToken)
    {
        var nameColumn = recipientCulture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        var sortable = new Dictionary<string, Expression<Func<BusinessFormVersion, object?>>>(StringComparer.OrdinalIgnoreCase)
            { ["name"] = version => EF.Property<string>(version, nameColumn) };
        var query = Active();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            query = query.Where(version => EF.Functions.ILike(EF.Property<string>(version, nameColumn), pattern, LikePatterns.EscapeCharacter));
        }
        return query.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("name", false), version => version.Id)
            .Select(version => new StructuredFormSelectionRow(version.DefinitionId, version.Id, version.Number,
                dbContext.Set<BusinessFormDefinition>().Where(definition => definition.Id == version.DefinitionId).Select(definition => definition.Prefix).Single(),
                ReadTexts(version.NameJson!), version.QuestionCount!.Value, ReadTexts(version.SubmitLabelJson!)))
            .ToPagedResultAsync(request, cancellationToken);
    }
    private IQueryable<BusinessFormVersion> Active() => dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => version.NameJson != null
        && version.SubmitLabelJson != null && version.QuestionCount != null && version.Culture != null
        && dbContext.Set<BusinessFormDefinition>().Any(definition => definition.Id == version.DefinitionId && definition.ActiveVersionId == version.Id)
        && dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.DefinitionId == version.DefinitionId
            && operation.VersionId == version.Id && operation.State == FormPublicationState.Published));
    private IQueryable<BusinessFormVersion> Published() => dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => version.NameJson != null
        && version.SubmitLabelJson != null && version.QuestionCount != null && version.Culture != null
        && dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.DefinitionId == version.DefinitionId
            && operation.VersionId == version.Id && operation.State == FormPublicationState.Published));
    private static Dictionary<string, string> ReadTexts(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The sealed form presentation is invalid.");
}
