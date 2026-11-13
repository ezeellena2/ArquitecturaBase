using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Configuration;

/// <summary>
/// <c>BusinessForms:DocumentReread</c> (DOC6): how many documents the re-reading after publishing reads a UTC day for each person who asked
/// for forms, out of that person's own daily quota of the documents channel, so the rest stays for their inbox (asking for a form, changing
/// a document's form); the others continue the next day. 1 to 100, validated at start; 5 by default, half of the engine's default daily
/// quota per person (the engine does not publish its quota through a port).
/// </summary>
public sealed class FormDocumentRereadOptions
{
    public const string Section = "BusinessForms:DocumentReread";

    [Range(1, 100)]
    public int DailyPerAsker { get; init; } = 5;
}
