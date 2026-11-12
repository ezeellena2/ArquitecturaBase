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
        var matching = Filter(definition, filter, request.Dimension is "choice" or "presence" ? request.FieldId : null);
        // The options come from every response of the form, or only from the owner's: whoever only loads sees nothing of others.
        var universe = dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(record => record.DefinitionId == definition.Id);
        var owned = filter.OwnerUserId is { } owner ? universe.Where(record => record.ActorUserId == owner) : null;
        IQueryable<FacetCountRow> options;
        if (request.Dimension == "actor")
        {
            options = (owned ?? universe).Select(record => record.ActorUserId).Distinct().Select(id => new FacetCountRow { Id = id.ToString(), Label = dbContext.Users.Where(user => user.Id == id).Select(user => user.DisplayName).FirstOrDefault() ?? "", Count = matching.Count(record => record.ActorUserId == id) });
        }
        else if (request.Dimension == "version")
        {
            options = ConstantOptions(definition.Versions.Select(version => new FacetLabel(version.Id.ToString(),
                    version.Number.ToString(CultureInfo.InvariantCulture), version.Number)).ToArray())
                .Select(option => new FacetCountRow { Id = option.Id, Label = option.Label, VersionNumber = option.NumericOrder,
                    Count = matching.Count(record => record.VersionId.ToString() == option.Id) });
        }
        else
        {
            var fieldId = request.FieldId!.Value;
            var field = FormResponseQuery.Questions(definition).First(question => question.FieldId == fieldId);
            // A question of the block counts the responses with an item that has the option and meets, in that same item, the
            // other conditions of the block.
            var values = dbContext.Set<BusinessFormRecordValue>().Where(value => value.FieldId == fieldId);
            if (field.Repeated == true && FormResponseQuery.SameItem(FormResponseQuery.ItemConditions(dbContext, definition, filter, fieldId)) is { } item)
                values = values.Where(value => item.Any(same => same.RecordId == value.RecordId && same.Row == value.Row));
            if (request.Dimension == "presence")
            {
                var versions = definition.Versions.Where(version => definition.Schemas[version.Id].Questions.Any(question => question.FieldId == fieldId)).Select(version => version.Id).ToArray();
                options = ConstantOptions([new("answered", "answered"), new("unanswered", "unanswered"), new("notAsked", "notAsked")])
                    .Select(option => new FacetCountRow { Id = option.Id, Label = option.Label, Count = option.Id == "notAsked" ? matching.Count(record => !versions.Contains(record.VersionId))
                        : option.Id == "answered" ? matching.Count(record => dbContext.Set<BusinessFormRecordValue>().Any(value => value.RecordId == record.Id && value.FieldId == fieldId)
                            || dbContext.Set<BusinessFormRecordFile>().Any(value => value.RecordId == record.Id && value.FieldId == fieldId))
                        : matching.Count(record => !dbContext.Set<BusinessFormRecordValue>().Any(value => value.RecordId == record.Id && value.FieldId == fieldId)
                            && !dbContext.Set<BusinessFormRecordFile>().Any(value => value.RecordId == record.Id && value.FieldId == fieldId)) });
            }
            else if (field.Type == "boolean")
            {
                options = ConstantOptions([new("true", "true"), new("false", "false")])
                    .Select(option => new FacetCountRow { Id = option.Id, Label = option.Label, Count = matching.Count(record => values
                        .Any(value => value.RecordId == record.Id && value.BooleanValue == (option.Id == "true"))) });
            }
            else
            {
                var choices = field.Source?.Catalog is not null
                    ? CatalogOptions(definition,fieldId,field.Source.Catalog.Value,filter.DisplayCulture,owned)
                    : ConstantOptions(FormResponseQuery.QuestionVersions(definition).Where(item => item.Question.FieldId == fieldId)
                        .SelectMany(item => (item.Question.Options ?? []).Select(option => (item.Version, Option: option)))
                        .DistinctBy(item => item.Option.ChoiceId).Select(item => new FacetLabel(item.Option.ChoiceId.ToString(),
                            Text(item.Option.Label, item.Version.DisplayCulture(filter.DisplayCulture)))).ToArray());
                options = choices.Select(option => new FacetCountRow { Id = option.Id, Label = option.Label, Count = matching.Count(record => values
                    .Any(value => value.RecordId == record.Id && value.ChoiceId != null && value.ChoiceId.Value.ToString() == option.Id)) });
            }
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            options = options.Where(option => EF.Functions.ILike(option.Label, pattern, LikePatterns.EscapeCharacter));
        }
        var sortable = new Dictionary<string, Expression<Func<FacetCountRow, object?>>>(StringComparer.OrdinalIgnoreCase)
            { ["label"] = request.Dimension == "version" ? option => option.VersionNumber : option => option.Label };
        return options.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("label", false), option => option.Id).Select(option => new FormResponseFacetOptionResponse(option.Id, option.Label, option.Count)).ToPagedResultAsync(request, cancellationToken);
    }
    private IQueryable<FacetLabel> CatalogOptions(FormResponseDefinitionRow definition,Guid fieldId,Guid catalogId,string culture,IQueryable<BusinessFormRecord>? owned)
    {
        var labels = FormResponseQuery.ReferenceLabels(dbContext, definition, culture).Where(value=>value.FieldId==fieldId&&value.CatalogId==catalogId);
        if (owned is not null) labels = labels.Where(value=>owned.Any(record=>record.Id==value.RecordId));
        return labels.GroupBy(value=>value.ItemId).Select(group=>new FacetLabel{Id=group.Key.ToString(),Label=group.OrderByDescending(value=>value.Id).Select(value=>value.Label).First()});
    }
    private IQueryable<FacetLabel> ConstantOptions(IReadOnlyList<FacetLabel> options) => dbContext.Database.SqlQueryRaw<FacetLabel>(
        "SELECT value->>'id' AS \"Id\", value->>'label' AS \"Label\", (value->>'numericOrder')::bigint AS \"NumericOrder\" FROM jsonb_array_elements({0}::jsonb) AS value", JsonSerializer.Serialize(options, JsonSerializerOptions.Web));
    private sealed class FacetCountRow
    {
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
        public int Count { get; init; }
        public long VersionNumber { get; init; }
    }
    private sealed class FacetLabel
    {
        public long NumericOrder { get; init; }
        public FacetLabel() { }
        public FacetLabel(string id, string label, long numericOrder = 0) { Id = id; Label = label; NumericOrder = numericOrder; }
        public string Id { get; init; } = "";
        public string Label { get; init; } = "";
    }
}
