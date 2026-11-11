using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.Arca;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.Arca.Validation.Invoicing;

/// <summary>Antes de consultar a ARCA: un punto de venta de 1 a 99998 y una revisión leída.</summary>
internal sealed class ChooseArcaPointOfSaleRequestValidator : AbstractValidator<ChooseArcaPointOfSaleRequest>
{
    public ChooseArcaPointOfSaleRequestValidator()
    {
        RuleFor(request => request.PointOfSale).InclusiveBetween(1, 99998)
            .WithMessage(_ => ErrorMessages.Get(ArcaInvoicingErrors.PointOfSaleInvalid.Code));
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.SettingsRevisionInvalid);
    }
}
