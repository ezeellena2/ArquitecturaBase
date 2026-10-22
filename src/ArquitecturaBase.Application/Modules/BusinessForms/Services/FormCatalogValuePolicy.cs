using System.Globalization;
using System.Text.RegularExpressions;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static partial class FormCatalogValuePolicy
{
    public static Result<string?> Normalize(BusinessFormCatalogValueType type, string? input)
    {
        var value = input?.Trim();
        if (type == BusinessFormCatalogValueType.Text)
            return string.IsNullOrEmpty(value) ? Result.Success<string?>(null) : FormCatalogErrors.ItemValueInvalid;
        if (!Enum.IsDefined(type)) return FormCatalogErrors.ItemValueInvalid;
        if (string.IsNullOrEmpty(value)) return FormCatalogErrors.ItemValueRequired;
        if (value.Length > 64) return FormCatalogErrors.ItemValueInvalid;
        switch (type)
        {
            case BusinessFormCatalogValueType.Integer:
                if (!IntegerPattern().IsMatch(value)) return FormCatalogErrors.ItemValueInvalid;
                return long.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            case BusinessFormCatalogValueType.Decimal:
                if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var number)) return FormCatalogErrors.ItemValueInvalid;
                var canonical = number.ToString("0.############################", CultureInfo.InvariantCulture);
                return FormScalarPrefillPolicy.CanonicalDecimal(value) == canonical
                    ? canonical : FormCatalogErrors.ItemValueInvalid;
            case BusinessFormCatalogValueType.Date:
                return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : FormCatalogErrors.ItemValueInvalid;
            default:
                return FormCatalogErrors.ItemValueInvalid;
        }
    }

    [GeneratedRegex("^-?[0-9]{1,18}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerPattern();
}
