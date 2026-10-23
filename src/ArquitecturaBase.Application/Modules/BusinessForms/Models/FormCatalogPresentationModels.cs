namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormCatalogPresentation(IReadOnlyList<string> Title, IReadOnlyList<string> Description);

public sealed record FormCatalogShown(string Title, string? Description);
