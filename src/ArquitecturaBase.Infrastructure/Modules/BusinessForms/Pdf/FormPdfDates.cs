using System.Globalization;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

/// <summary>
/// The one way the copy writes a date: numbers only, day first in Spanish and month first in English, never the
/// month as a word. A moment adds its time after a comma, 24 hours in Spanish and AM or PM in English. The screens of
/// the front and the Excel write them the same way (<c>formatDateInZone</c> and <c>formatDateTimeInZone</c>).
/// </summary>
internal static class FormPdfDates
{
    public static string Day(DateOnly value, string culture) => value.ToString(Pattern(culture), CultureInfo.InvariantCulture);

    public static string Moment(DateTime value, string culture) =>
        value.ToString(Pattern(culture) + (culture == "en" ? ", h:mm tt" : ", H:mm"), CultureInfo.InvariantCulture);

    private static string Pattern(string culture) => culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy";
}
