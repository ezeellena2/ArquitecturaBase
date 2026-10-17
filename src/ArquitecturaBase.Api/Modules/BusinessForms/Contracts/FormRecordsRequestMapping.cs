using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

internal static class FormRecordsRequestMapping
{
    public static ListFormRecordsRequest Map(FormRecordsSearchHttpRequest request) => new()
    {
        Page = request.Page, PageSize = request.PageSize, Sort = request.Sort, SortFieldId = request.SortFieldId,
        VersionIds = request.VersionIds ?? [], ActorIds = request.ActorIds ?? [], ReceivedFromUtc = request.ReceivedFromUtc, ReceivedToUtc = request.ReceivedToUtc,
        State = request.State, Search = request.Search, Conditions = request.Conditions?.Select(condition => condition is null ? null! : new FormRecordCondition(condition.FieldId, condition.Operator, condition.Values)).ToArray() ?? [],
    };
}
