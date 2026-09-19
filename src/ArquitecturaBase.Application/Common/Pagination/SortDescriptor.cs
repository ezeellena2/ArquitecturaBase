namespace ArquitecturaBase.Application.Common.Pagination;

public sealed record SortDescriptor(string Field, bool Descending)
{
    /// <summary>Interpreta "campo" o "-campo". Devuelve null si no hay campo.</summary>
    public static SortDescriptor? Parse(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return null;
        }

        var trimmed = sort.Trim();
        var descending = trimmed.StartsWith('-');
        var field = (descending ? trimmed[1..] : trimmed).Trim();

        return field.Length == 0 ? null : new SortDescriptor(field, descending);
    }
}
