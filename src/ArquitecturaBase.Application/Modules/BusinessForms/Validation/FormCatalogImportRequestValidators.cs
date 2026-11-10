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
        if (request.SheetSha256 is { } hash && !FormCatalogImportRequestValidation.IsSha256(hash)) context.AddFailure("sheetSha256", message);
        if (request.ConfirmDeactivations is < 0) context.AddFailure("confirmDeactivations", message);
    });
}

// Stage 2 of the assistant authoring: the turn's request and the proposal instead of the file; replacing is the only mode.
internal sealed class FormCatalogAssistantImportRequestValidator : AbstractValidator<FormCatalogAssistantImportRequest>
{
    public FormCatalogAssistantImportRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        var message = FormCatalogRequestValidation.InvalidMessage();
        if (request.RequestId is null || request.RequestId == Guid.Empty) context.AddFailure("requestId", message);
        if (request.ProposalId is null || request.ProposalId == Guid.Empty) context.AddFailure("proposalId", message);
        if (request.ExpectedRevision is null or < 1 or > 9007199254740991) context.AddFailure("expectedRevision", message);
        if (request.SheetSha256 is { } hash && !FormCatalogImportRequestValidation.IsSha256(hash)) context.AddFailure("sheetSha256", message);
        if (request.ConfirmDeactivations is < 0) context.AddFailure("confirmDeactivations", message);
    });
}

internal static class FormCatalogImportRequestValidation
{
    // A SHA-256 as the reader writes it: 64 lowercase hexadecimal characters.
    public static bool IsSha256(string hash) =>
        hash.Length == 64 && hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
