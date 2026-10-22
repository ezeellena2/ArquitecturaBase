using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed partial class BusinessFormCatalog
{
    public string? LastImportSha256 { get; private set; }
    public DateTime? LastImportAtUtc { get; private set; }

    public Result MarkImported(long expectedRevision, string sha256, DateTime atUtc)
    {
        if (Kind != BusinessFormCatalogKind.Manual) return FormCatalogErrors.ManualItemsUnavailable;
        if (Revision != expectedRevision || Revision >= 9007199254740991) return FormCatalogErrors.RevisionConflict;
        if (atUtc.Kind != DateTimeKind.Utc || sha256 is not { Length: 64 }
            || sha256.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            return FormCatalogErrors.ContentInvalid;
        LastImportSha256 = sha256;
        LastImportAtUtc = atUtc;
        Revision++;
        return Result.Success();
    }
}
