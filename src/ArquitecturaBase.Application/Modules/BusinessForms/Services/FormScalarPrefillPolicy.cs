using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormScalarPrefillPolicy
{
    public static Result<string> Normalize(StructuredFormQuestion question, string value, IReadOnlyList<StructuredFormCatalogOffer> catalogs)
    {
        if (string.IsNullOrEmpty(value)) return FormRecordErrors.InvalidResponse;
        switch (question.Type)
        {
            case "shortText":
            case "longText":
                return question.MaxLength is { } maximum && value.Length <= maximum && StorableText.IsStorable(value)
                    ? value : FormRecordErrors.InvalidResponse;
            case "decimal":
                if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                    return FormRecordErrors.InvalidResponse;
                var canonical = number.ToString("0.############################", CultureInfo.InvariantCulture);
                return CanonicalDecimal(value) == CanonicalDecimal(canonical) ? canonical : FormRecordErrors.InvalidResponse;
            case "date":
                return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : FormRecordErrors.InvalidResponse;
            case "boolean":
                return bool.TryParse(value, out var boolean) ? boolean ? "true" : "false" : FormRecordErrors.InvalidResponse;
            case "choice":
                if (!Guid.TryParseExact(value, "D", out var choice)) return FormRecordErrors.InvalidResponse;
                var offered = question.SourceCatalogId is { } catalogId
                    ? catalogs.FirstOrDefault(offer => offer.FieldId == question.FieldId && offer.CatalogId == catalogId)?.Options.Any(option => option.Id == choice) == true
                    : question.Options?.Any(option => option.ChoiceId == choice) == true;
                return offered ? choice.ToString("D") : FormRecordErrors.InvalidResponse;
            default: return FormRecordErrors.InvalidResponse;
        }
    }

    internal static string CanonicalDecimal(string value)
    {
        var negative = value[0] == '-';
        var unsigned = value.TrimStart('-', '+');
        var point = unsigned.IndexOf('.');
        var integer = (point < 0 ? unsigned : unsigned[..point]).TrimStart('0');
        var fraction = (point < 0 ? "" : unsigned[(point + 1)..]).TrimEnd('0');
        var normalized = (integer.Length == 0 ? "0" : integer) + (fraction.Length == 0 ? "" : "." + fraction);
        return negative && normalized != "0" ? "-" + normalized : normalized;
    }
}
