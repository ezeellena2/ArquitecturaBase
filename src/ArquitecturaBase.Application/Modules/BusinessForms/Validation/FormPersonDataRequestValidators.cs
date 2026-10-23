using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class CreateFormPersonAttributeRequestValidator : AbstractValidator<CreateFormPersonAttributeRequest>
{
    public CreateFormPersonAttributeRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        if (request.Key is not { Length: > 0 and <= 40 } key || key[0] is < 'a' or > 'z' || key.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
            context.AddFailure("key", FormPersonDataValidation.Message());
        FormPersonDataValidation.Attribute(request.Name, request.Type, request.SourceCatalogId, context.AddFailure);
    });
}
internal sealed class UpdateFormPersonAttributeRequestValidator : AbstractValidator<UpdateFormPersonAttributeRequest>
{
    public UpdateFormPersonAttributeRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        if (request.ExpectedRevision is null or < 1 or > 9007199254740991) context.AddFailure("expectedRevision", FormPersonDataValidation.Message());
        if (request.Position is < 0 or >= BusinessFormPersonAttribute.MaximumAttributes) context.AddFailure("position", FormPersonDataValidation.Message());
        FormPersonDataValidation.Attribute(request.Name, request.Type, request.SourceCatalogId, context.AddFailure);
    });
}
internal sealed class UpdateFormPersonDataRequestValidator : AbstractValidator<UpdateFormPersonDataRequest>
{
    public UpdateFormPersonDataRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        if (request.ExpectedRevision is < 1 or > 9007199254740991) context.AddFailure("expectedRevision", FormPersonDataValidation.Message());
        if (request.Values is null || request.Values.Count > BusinessFormPersonAttribute.MaximumAttributes)
        { context.AddFailure("values", FormPersonDataValidation.Message()); return; }
        foreach (var (key, value) in request.Values)
            if (!Guid.TryParseExact(key, "D", out var id) || id == Guid.Empty || key != id.ToString("D") || value is not null && (value.Trim().Length > 300 || !StorableText.IsStorable(value)))
                context.AddFailure("values." + key, FormPersonDataValidation.Message());
    });
}
internal sealed class ListFormPersonDataRequestValidator : PagedRequestValidator<ListFormPersonDataRequest>
{ public ListFormPersonDataRequestValidator() : base(ListFormPersonDataRequest.SortableFields) { } }
internal sealed class ListFormPersonAttributeOptionsRequestValidator : PagedRequestValidator<ListFormPersonAttributeOptionsRequest>
{ public ListFormPersonAttributeOptionsRequestValidator() : base(ListFormPersonAttributeOptionsRequest.SortableFields) { } }
internal static class FormPersonDataValidation
{
    public static string Message() => ErrorMessages.Get(FormPersonDataErrors.AttributeInvalid.Code);
    public static void Attribute(IReadOnlyDictionary<string, string?>? names, string? type, Guid? source, Action<string, string> fail)
    {
        if (names is null || !names.TryGetValue("es", out var original) || string.IsNullOrWhiteSpace(original)) fail("name.es", Message());
        if (names is not null) foreach (var (culture, value) in names)
            if (culture is not ("es" or "en") || value is not null && (value.Trim().Length > 120 || !StorableText.IsStorable(value))) fail("name." + culture, Message());
        if (type is not ("Catalog" or "Text")) fail("type", Message());
        if (type == "Catalog" ? source is null || source == Guid.Empty : source is not null) fail("sourceCatalogId", Message());
    }
}
