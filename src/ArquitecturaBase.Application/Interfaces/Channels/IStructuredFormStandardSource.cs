using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// The standards a module brings to the forms library (BI1-A), as data. An optional collection: without sources the library is
/// empty and does not show. Keys are stable and unique across sources; a new version of a standard is another <c>Version</c>.
/// </summary>
public interface IStructuredFormStandardSource
{
    IReadOnlyList<StructuredFormStandard> Standards { get; }

    /// <summary>DOC1: the standards of the next stages, without a draft, that the library lists as «Próximamente»; none by default.</summary>
    IReadOnlyList<StructuredFormUpcomingStandard> Upcoming => [];
}
