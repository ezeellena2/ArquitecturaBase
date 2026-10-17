using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed partial class FormResponseReader
{
    private IQueryable<ReferenceLabelRow> ReferenceLabels(string culture)=>dbContext.Database.SqlQueryRaw<ReferenceLabelRow>(
        """
        SELECT "Id", "RecordId", "FieldId", "CatalogId", "ItemId",
            COALESCE("TranslationsJson" -> {0} ->> 'text', "TranslationsJson" -> 'es' ->> 'text', "Text") AS "Label"
        FROM "BusinessFormRecordReferences"
        """,culture);
    private sealed class ReferenceLabelRow
    {
        public Guid Id { get; init; }
        public Guid RecordId { get; init; }
        public Guid FieldId { get; init; }
        public Guid CatalogId { get; init; }
        public Guid ItemId { get; init; }
        public string Label { get; init; }="";
    }
}
