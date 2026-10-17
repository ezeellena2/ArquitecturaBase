using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormResponseAssistancePolicy
{
    public static FormResponseAssistanceResponse? Project(FormResponseStoredRow row, string culture)
    {
        if (row.CaptureOriginJson is null) return null;
        var snapshot = FormDraftPolicy.Read<FormRecordCaptureOriginSnapshot>(row.CaptureOriginJson);
        var schema = FormDraftPolicy.Read<FormVersionSchema>(row.Record.SchemaJson);
        var values = snapshot.Origin.ProposedValues.Select(value =>
        {
            var question = schema.Questions.Single(question => question.FieldId == value.FieldId);
            object typed = question.Type switch
            {
                "decimal" => decimal.Parse(value.Value, CultureInfo.InvariantCulture),
                "boolean" => bool.Parse(value.Value),
                _ => value.Value
            };
            var proposed = new StructuredRecordReadValue(question.FieldId, question.Key, question.Type,
                FormRecordPresentationPolicy.Text(question.Label, culture),
                FormRecordPresentationPolicy.Decode(question, JsonSerializer.SerializeToElement(typed), culture, snapshot.ProposedReferences));
            var included = snapshot.Origin.IncludedFieldIds.Contains(value.FieldId);
            return new FormResponseAssistedValueResponse(proposed, included, snapshot.ChangedFieldIds.Contains(value.FieldId),
                included && snapshot.Origin.Suggestion?.Mappings.Any(mapping => mapping.DestinationFieldId == value.FieldId) == true);
        }).ToArray();
        return new(snapshot.Origin.SenderKind.ToString(), snapshot.Origin.OperatorUserId is { } operatorId ? new(operatorId, null) : null,
            snapshot.Origin.Reason, values);
    }
}
