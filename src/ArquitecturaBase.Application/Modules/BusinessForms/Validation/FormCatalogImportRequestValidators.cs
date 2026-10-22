using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class FormCatalogImportRequestValidator : AbstractValidator<FormCatalogImportRequest>
{
    public FormCatalogImportRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        var message = FormCatalogRequestValidation.InvalidMessage();
        if (request.ExpectedRevision is < 1 or > 9007199254740991) context.AddFailure("expectedRevision", message);
        if (request.Mode is not ("Replace" or "Append")) context.AddFailure("mode", message);
        if (request.Content is null || !request.Content.CanRead) context.AddFailure("content", message);
        if (string.IsNullOrWhiteSpace(request.FileName)) context.AddFailure("fileName", message);
        if (request.SheetSha256 is { } hash && (hash.Length != 64
            || hash.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))))
            context.AddFailure("sheetSha256", message);
        if (request.ConfirmDeactivations is < 0) context.AddFailure("confirmDeactivations", message);
    });
}
