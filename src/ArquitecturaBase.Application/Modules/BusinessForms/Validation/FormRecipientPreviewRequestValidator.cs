using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class FormRecipientPreviewRequestValidator : AbstractValidator<FormRecipientPreviewRequest>
{
    public FormRecipientPreviewRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        void Fail(string path) => context.AddFailure(path, ErrorMessages.Get(FormDefinitionErrors.ContentInvalid.Code));
        if (request.Type is not ("choice" or "multiChoice")) Fail("type");
        if (request.SampleUserId == Guid.Empty) Fail("sampleUserId");
        if (request.Culture is not ("es" or "en")) Fail("culture");
        if (request.Source?.Catalog is null || request.Source.Catalog == Guid.Empty) Fail("source.catalog");
        if (!FormCatalogMatchPolicy.HasValidShape(request.Source?.Match)) Fail("source.match");
        if (!FormCatalogPresentationPolicy.HasValidShape(request.Source?.Presentation)) Fail("source.presentation");
    });
}

internal sealed class FormRecipientMaximumRequestValidator : AbstractValidator<FormRecipientMaximumRequest>
{
    public FormRecipientMaximumRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        void Fail(string path) => context.AddFailure(path, ErrorMessages.Get(FormDefinitionErrors.ContentInvalid.Code));
        if (request.Source?.Catalog is null || request.Source.Catalog == Guid.Empty) Fail("source.catalog");
        if (request.Source?.Match is not { Count: > 0 } || !FormCatalogMatchPolicy.HasValidShape(request.Source.Match)) Fail("source.match");
    });
}

internal sealed class ListFormRecipientSamplesRequestValidator : PagedRequestValidator<ListFormRecipientSamplesRequest>
{
    public ListFormRecipientSamplesRequestValidator() : base(ListFormRecipientSamplesRequest.SortableFields) { }
}
