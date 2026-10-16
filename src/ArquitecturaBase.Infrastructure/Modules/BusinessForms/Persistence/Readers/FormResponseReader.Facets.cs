using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed partial class FormResponseReader
{
    public Task<PagedResult<FormResponseFacetOptionResponse>> ListFacetsAsync(FormResponseDefinitionRow definition, FormRecordFacetRequest request, CancellationToken cancellationToken)
    {
        var filter = request.Filter with
        {
            ActorIds = request.Dimension == "actor" ? [] : request.Filter.ActorIds,
            VersionIds = request.Dimension == "version" ? [] : request.Filter.VersionIds,
        };
        var matching = Filter(definition, filter, request.FieldId);
        IQueryable<FacetCountRow> options;
        if (request.Dimension == "actor")
        {
            options = dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(record => record.DefinitionId == definition.Id)
                .Select(record => record.ActorUserId).Distinct().Select(id => new FacetCountRow { Id = id.ToString(), Label = dbContext.Users.Where(user => user.Id == id).Select(user => user.DisplayName).FirstOrDefault() ?? "", Count = matching.Count(record => record.ActorUserId == id) });
        }
        else if (request.Dimension == "version")
        {
            options = ConstantOptions(definition.Versions.Select(version => new FacetLabel(version.Id.ToString(), version.Number.ToString(CultureInfo.InvariantCulture))).ToArray())
                .Select(option => new FacetCountRow { Id = option.Id, Label = option.Label, Count = matching.Count(record => record.VersionId.ToString() == option.Id) });
        }
        else
        {
            var fieldId = request.FieldId!.Value;
            var field = Questions(definition).First(question => question.FieldId == fieldId);
            if (request.Dimension == "presence")
            {
                var versions = definition.Versions.Where(version => Schema(version).Questions.Any(question => question.FieldId == fieldId)).Select(version => version.Id).ToArray();
                options = ConstantOptions([new("answered", "answered"), new("unanswered", "unanswered"), new("notAsked", "notAsked")])
                    .Select(option => new FacetCountRow { Id = option.Id, Label = option.Label, Count = option.Id == "notAsked" ? matching.Count(record => !versions.Contains(record.VersionId))
                        : option.Id == "answered" ? matching.Count(record => dbContext.Set<BusinessFormRecordValue>().Any(value => value.RecordId == record.Id && value.FieldId == fieldId)
                            || dbContext.Set<BusinessFormRecordFile>().Any(value => value.RecordId == record.Id && value.FieldId == fieldId))
                        : matching.Count(record => versions.Contains(record.VersionId) && !dbContext.Set<BusinessFormRecordValue>().Any(value => value.RecordId == record.Id && value.FieldId == fieldId)
                            && !dbContext.Set<BusinessFormRecordFile>().Any(value => value.RecordId == record.Id && value.FieldId == fieldId)) });
            }
            else if (field.Type == "boolean")
            {
                options = ConstantOptions([new("true", "true"), new("false", "false")])
                    .Select(option => new FacetCountRow { Id = option.Id, Label = option.Label, Count = matching.Count(record => dbContext.Set<BusinessFormRecordValue>()
                        .Any(value => value.RecordId == record.Id && value.FieldId == fieldId && value.BooleanValue == (option.Id == "true"))) });
            }
            else
            {
                var choices = field.Source?.Catalog is not null
                    ? dbContext.Set<BusinessFormRecordReference>().AsNoTracking().Where(value => value.FieldId == fieldId && value.CatalogId == field.Source.Catalog)
                        .GroupBy(value => value.ItemId).Select(group => new FacetLabel(group.Key.ToString(), group.OrderByDescending(value => value.Id).Select(value => value.Text).First()))
                    : ConstantOptions(Questions(definition).Where(question => question.FieldId == fieldId).SelectMany(question => question.Options ?? [])
                        .DistinctBy(option => option.ChoiceId).Select(option => new FacetLabel(option.ChoiceId.ToString(), option.Label[filter.DisplayCulture])).ToArray());
                options = choices.Select(option => new FacetCountRow { Id = option.Id, Label = option.Label, Count = matching.Count(record => dbContext.Set<BusinessFormRecordValue>()
                    .Any(value => value.RecordId == record.Id && value.FieldId == fieldId && value.ChoiceId != null && value.ChoiceId.Value.ToString() == option.Id)) });
            }
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            options = options.Where(option => EF.Functions.ILike(option.Label, pattern, LikePatterns.EscapeCharacter));
        }
        var sortable = new Dictionary<string, Expression<Func<FacetCountRow, object?>>>(StringComparer.OrdinalIgnoreCase) { ["label"] = option => option.Label };
        return options.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("label", false), option => option.Id).Select(option => new FormResponseFacetOptionResponse(option.Id, option.Label, option.Count)).ToPagedResultAsync(request, cancellationToken);
    }
    private IQueryable<FacetLabel> ConstantOptions(IReadOnlyList<FacetLabel> options) => dbContext.Database.SqlQueryRaw<FacetLabel>(
        "SELECT value->>'id' AS \"Id\", value->>'label' AS \"Label\" FROM jsonb_array_elements({0}::jsonb) AS value", JsonSerializer.Serialize(options, JsonSerializerOptions.Web));
    private sealed class FacetCountRow
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
        public int Count { get; init; }
    }
    private sealed record FacetLabel(string Id, string Label);
}
