using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

/// <summary>
/// What formularios brings to its own library (DOC1), as data (<see cref="FormStandardResources"/>): «Comprobante de transferencia»,
/// which reads receipts, validates the account and the CUIT of each side and brings the catalog «Bancos y billeteras» built from the
/// table of entities of the BCRA; and the standards of the next stages, without a draft, as «Próximamente».
/// </summary>
internal sealed class BusinessFormStandardSource : IStructuredFormStandardSource
{
    public IReadOnlyList<StructuredFormStandard> Standards { get; } = [FormStandardResources.TransferReceipt()];

    public IReadOnlyList<StructuredFormUpcomingStandard> Upcoming { get; } = FormStandardResources.Upcoming();
}
