using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.Arca;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.Arca.Validation.Invoicing;

/// <summary>Antes del límite: el número, sin espacios a los costados, de 1 a 30 caracteres; el día; y una revisión leída.</summary>
internal sealed class SetArcaVoucherDataRequestValidator : AbstractValidator<SetArcaVoucherDataRequest>
{
    public SetArcaVoucherDataRequestValidator()
    {
        RuleFor(request => request.GrossIncomeNumber).Must(number => number?.Trim() is { Length: > 0 and <= 30 })
            .WithMessage(_ => ErrorMessages.Get(ArcaInvoicingErrors.GrossIncomeInvalid.Code));
        RuleFor(request => request.ActivityStartOn).NotEqual(default(DateOnly))
            .WithMessage(_ => ErrorMessages.Get(ArcaInvoicingErrors.ActivityStartInvalid.Code));
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.SettingsRevisionInvalid);
    }
}
