using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What the library offers (BI1-A, DOC1): the standards of the installed modules, whose keys are unique among them and the next ones,
/// and the standards of the next stages. A standard with a photo or document question (the receipt of «Comprobante de transferencia»)
/// is offered only while private storage is available, which publishing it needs (<see cref="FormPublicationPolicy.ValidateFiles"/>):
/// «Agregar» never makes a copy that cannot be published.
/// </summary>
internal sealed class FormLibraryOfferPolicy
{
    private readonly (StructuredFormStandard Standard, bool HasFiles)[] _standards;
    private readonly IPrivateFileStore _files;

    public FormLibraryOfferPolicy(IEnumerable<IStructuredFormStandardSource> sources, IPrivateFileStore files)
    {
        var installed = sources.ToArray();
        var standards = installed.SelectMany(source => source.Standards).ToArray();
        Upcoming = installed.SelectMany(source => source.Upcoming).ToArray();
        if (standards.Select(standard => standard.Key).Concat(Upcoming.Select(standard => standard.Key)).Distinct(StringComparer.Ordinal).Count()
            != standards.Length + Upcoming.Count)
            throw new InvalidOperationException("Library standard keys must be unique.");
        _standards = standards.Select(standard => (standard,
            FormDraftPolicy.Read<FormDraftInput>(standard.DraftJson).Fields.Any(field => field.Type is "photo" or "document"))).ToArray();
        _files = files;
    }

    public IReadOnlyList<StructuredFormUpcomingStandard> Upcoming { get; }

    /// <summary>The standards «Agregar» can copy now, in the order their modules list them.</summary>
    public IReadOnlyList<StructuredFormStandard> Offered()
    {
        var storage = _files.IsAvailable;
        return _standards.Where(item => storage || !item.HasFiles).Select(item => item.Standard).ToArray();
    }
}
