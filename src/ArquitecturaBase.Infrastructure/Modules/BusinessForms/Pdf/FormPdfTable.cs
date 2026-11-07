namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

/// <summary>
/// The items of a block of rows in the PDF: the name of the block, a header per question of its version, which of those columns
/// are numbers (right-aligned, like the number of the item) and the text of each cell, empty where the item left it unanswered.
/// </summary>
internal sealed record FormPdfTable(string Title, IReadOnlyList<string> Headers, IReadOnlyList<bool> Numeric, IReadOnlyList<IReadOnlyList<string>> Rows);
