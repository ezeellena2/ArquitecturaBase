using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class CreateManualCatalogRequestValidator : AbstractValidator<CreateManualCatalogRequest>
{
    public CreateManualCatalogRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        if (request.ValueType is not (null or "Text" or "Integer" or "Decimal" or "Date"))
            context.AddFailure("valueType", FormCatalogRequestValidation.InvalidMessage());
        FormCatalogRequestValidation.Validate(request.Name, request.Description, request.Items, context, creating: true);
        FormCatalogRequestValidation.ValidateConfiguration(request.Fields, request.Presentation, true, context.AddFailure);
    });
}

internal sealed class UpdateManualCatalogRequestValidator : AbstractValidator<UpdateManualCatalogRequest>
{
    public UpdateManualCatalogRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        if (request.ExpectedRevision is null or < 1 or > 9007199254740991)
            context.AddFailure("expectedRevision", FormCatalogRequestValidation.InvalidMessage());
        if (request.SortAlphabetically && request.Moves is { Count: > 0 })
            context.AddFailure("moves", FormCatalogRequestValidation.InvalidMessage());
        FormCatalogRequestValidation.Validate(request.Name, request.Description, request.Items, context, creating: false);
        FormCatalogRequestValidation.ValidateConfiguration(request.Fields, request.Presentation, false, context.AddFailure);
        if (request.ConfirmFieldRemovals is { } removals && (removals.Count > BusinessFormCatalog.MaxFields || removals.Distinct().Count() != removals.Count || removals.Contains(Guid.Empty)))
            context.AddFailure("confirmFieldRemovals", FormCatalogRequestValidation.InvalidMessage());
        if (request.Moves is { } moves)
        {
            if (moves.Count > 500) context.AddFailure("moves", FormCatalogRequestValidation.InvalidMessage());
            for (var index = 0; index < moves.Count; index++)
            {
                var move = moves[index];
                if (move is null) { context.AddFailure($"moves[{index}]", FormCatalogRequestValidation.InvalidMessage()); continue; }
                if (move.ItemId is null || move.ItemId == Guid.Empty)
                    context.AddFailure($"moves[{index}].itemId", FormCatalogRequestValidation.InvalidMessage());
                if (move.Direction is not ("up" or "down"))
                    context.AddFailure($"moves[{index}].direction", FormCatalogRequestValidation.InvalidMessage());
            }
        }
    });
}

internal sealed class FormCatalogPresentationCheckRequestValidator : AbstractValidator<FormCatalogPresentationCheckRequest>
{
    public FormCatalogPresentationCheckRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        if (request.Presentation is null) context.AddFailure("presentation", FormCatalogRequestValidation.InvalidMessage());
        FormCatalogRequestValidation.ValidateConfiguration(null, request.Presentation, false, context.AddFailure);
    });
}

internal sealed class FormCatalogFilterOptionsRequestValidator : PagedRequestValidator<FormCatalogFilterOptionsRequest>
{
    public FormCatalogFilterOptionsRequestValidator() : base(FormCatalogFilterOptionsRequest.SortableFields) { }
}

internal sealed class FormCatalogFilterPreviewRequestValidator : AbstractValidator<FormCatalogFilterPreviewRequest>
{
    public FormCatalogFilterPreviewRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        if (request.SampleUserId == Guid.Empty)
            context.AddFailure("sampleUserId", FormCatalogRequestValidation.InvalidMessage());
        FormCatalogFilterRequestValidation.Validate(request.Filters, context.AddFailure);
    });
}

internal sealed class CreateFiltersCatalogRequestValidator : AbstractValidator<CreateFiltersCatalogRequest>
{
    public CreateFiltersCatalogRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        FormCatalogFilterRequestValidation.ValidateTexts(request.Name, request.Description, context.AddFailure);
        FormCatalogFilterRequestValidation.Validate(request.Filters, context.AddFailure);
    });
}

internal sealed class UpdateFiltersCatalogRequestValidator : AbstractValidator<UpdateFiltersCatalogRequest>
{
    public UpdateFiltersCatalogRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        if (request.ExpectedRevision is null or < 1 or > 9007199254740991)
            context.AddFailure("expectedRevision", FormCatalogRequestValidation.InvalidMessage());
        FormCatalogFilterRequestValidation.ValidateTexts(request.Name, request.Description, context.AddFailure);
        FormCatalogFilterRequestValidation.Validate(request.Filters, context.AddFailure);
    });
}

internal static class FormCatalogFilterRequestValidation
{
    private static readonly HashSet<string> Columns = new(StringComparer.Ordinal) { "name", "role", "active" };
    private static readonly string InvalidMessage = FormCatalogRequestValidation.InvalidMessage();

    public static void ValidateTexts(IReadOnlyDictionary<string, string?>? name,
        IReadOnlyDictionary<string, string?>? description, Action<string, string> fail)
    {
        CheckMap(name, "name", 120, required: true, fail);
        CheckMap(description, "description", 1000, required: false, fail);
    }

    public static void Validate(FormCatalogFilterDefinition? filters, Action<string, string> fail)
    {
        if (filters is null)
        {
            fail("filters", InvalidMessage);
            return;
        }

        if (filters.TextColumn != "name")
            fail("filters.textColumn", InvalidMessage);
        if (filters.DetailColumn is not null && !Columns.Contains(filters.DetailColumn))
            fail("filters.detailColumn", InvalidMessage);
        if (filters.SortColumn is null || !Columns.Contains(filters.SortColumn))
            fail("filters.sortColumn", InvalidMessage);
        if (filters.Conditions is null)
        {
            fail("filters.conditions", InvalidMessage);
            return;
        }

        for (var index = 0; index < filters.Conditions.Count; index++)
        {
            var condition = filters.Conditions[index];
            var path = $"filters.conditions[{index}]";
            if (condition is null)
            {
                fail(path, InvalidMessage);
                continue;
            }

            var validOperator = (condition.Column, condition.Operator) switch
            {
                ("name", "contains" or "equals") => !string.IsNullOrWhiteSpace(condition.Value),
                ("role", "has") => Guid.TryParse(condition.Value, out var roleId) && roleId != Guid.Empty,
                ("active", "equals") => bool.TryParse(condition.Value, out _),
                _ => false,
            };
            if (!validOperator) fail(path, InvalidMessage);
        }
    }

    private static void CheckMap(IReadOnlyDictionary<string, string?>? values, string path, int maxLength, bool required,
        Action<string, string> fail)
    {
        if (values is null)
        {
            if (required) fail(path, InvalidMessage);
            return;
        }

        // Shape only: null or blank is a missing translation. The service requires the text in the catalog's original culture.
        foreach (var (culture, value) in values)
            if (culture is not ("es" or "en") || value?.Length > maxLength)
                fail(path + "." + culture, InvalidMessage);
    }
}

internal static partial class FormCatalogRequestValidation
{
    public static void Validate(IReadOnlyDictionary<string, string?>? name, IReadOnlyDictionary<string, string?>? description,
        IReadOnlyList<FormCatalogManualItemSaveRequest?>? items, ValidationContext<CreateManualCatalogRequest> context, bool creating)
    {
        ValidateCore(name, description, items, context.AddFailure, creating);
    }

    public static void Validate(IReadOnlyDictionary<string, string?>? name, IReadOnlyDictionary<string, string?>? description,
        IReadOnlyList<FormCatalogManualItemSaveRequest?>? items, ValidationContext<UpdateManualCatalogRequest> context, bool creating)
    {
        ValidateCore(name, description, items, context.AddFailure, creating);
    }

    private static void ValidateCore(IReadOnlyDictionary<string, string?>? name, IReadOnlyDictionary<string, string?>? description,
        IReadOnlyList<FormCatalogManualItemSaveRequest?>? items, Action<string, string> fail, bool creating)
    {
        // Shape only: null or blank is a missing translation. FormCatalogOriginalTextPolicy requires the original culture.
        void CheckMap(IReadOnlyDictionary<string, string?>? map, string path, int maxLength, bool required)
        {
            if (map is null) { if (required) fail(path, Message()); return; }
            foreach (var (language, value) in map)
                if (language is not ("es" or "en") || value?.Length > maxLength) fail(path + "." + language, Message());
        }

        CheckMap(name, "name", 120, required: true);
        CheckMap(description, "description", 1000, required: false);
        if (items is null) { fail("items", Message()); return; }
        if (items.Count > BusinessFormCatalog.MaxItems) { fail("items", ErrorMessages.Get(FormCatalogErrors.TooManyItems.Code)); return; }
        var localIds = new HashSet<string>(StringComparer.Ordinal);
        var itemIds = new HashSet<Guid>();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var path = $"items[{index}]";
            if (item is null) { fail(path, Message()); continue; }
            if (item.LocalId is null || !LocalIdPattern().IsMatch(item.LocalId) || !localIds.Add(item.LocalId)) fail(path + ".localId", Message());
            if (item.ItemId == Guid.Empty || (item.ItemId.HasValue && !itemIds.Add(item.ItemId.Value))) fail(path + ".itemId", Message());
            if (creating && item.ItemId.HasValue) fail(path + ".itemId", Message());
            if (string.IsNullOrWhiteSpace(item.Code) || item.Code.Length > 80) fail(path + ".code", Message());
            CheckMap(item.Text, path + ".text", BusinessFormCatalogItem.MaxTextLength, required: true);
            CheckMap(item.Detail, path + ".detail", BusinessFormCatalogItem.MaxDetailLength, required: false);
            if (item.Fields is { } fields)
            {
                if (fields.Count > BusinessFormCatalog.MaxFields) fail(path + ".fields", Message());
                foreach (var (key, value) in fields)
                    if (!Guid.TryParseExact(key, "D", out var id) || id == Guid.Empty || key != id.ToString("D") || value?.Length > 300)
                        fail(path + ".fields." + key, Message());
            }
        }
    }

    public static void ValidateConfiguration(IReadOnlyList<FormCatalogFieldSaveRequest?>? fields,
        FormCatalogPresentation? presentation, bool creating, Action<string, string> fail)
    {
        if (presentation is not null)
        {
            if (presentation.Title is not { Count: > 0 and <= 24 } || presentation.Title.Any(string.IsNullOrWhiteSpace)) fail("presentation.title", Message());
            if (presentation.Description is null || presentation.Description.Count > 24 || presentation.Description.Any(string.IsNullOrWhiteSpace)) fail("presentation.description", Message());
        }
        if (fields is null) return;
        if (fields.Count > BusinessFormCatalog.MaxFields) { fail("fields", ErrorMessages.Get(FormCatalogErrors.TooManyFields.Code)); return; }
        var ids = new HashSet<Guid>();
        var locals = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var path = $"fields[{index}]";
            if (field is null) { fail(path, Message()); continue; }
            if (field.Id == Guid.Empty || creating && field.Id is not null || field.Id is { } id && !ids.Add(id)) fail(path + ".id", Message());
            if (field.Id is null && (field.LocalId is null || !LocalIdPattern().IsMatch(field.LocalId) || !locals.Add(field.LocalId))) fail(path + ".localId", Message());
            if (field.Key is null || !FieldKeyPattern().IsMatch(field.Key)) fail(path + ".key", Message());
            if (field.Name is null || field.Name.Any(pair => pair.Key is not ("es" or "en") || pair.Value?.Length > 120)) fail(path + ".name", Message());
            if (field.Type is not ("text" or "integer" or "decimal" or "date" or "boolean" or "catalog")) fail(path + ".type", Message());
            if ((field.Type == "catalog") != field.SourceCatalogId.HasValue || field.SourceCatalogId == Guid.Empty) fail(path + ".sourceCatalogId", Message());
            if (field.Position < 0) fail(path + ".position", Message());
            if (field.Role is not ("data" or "filter")) fail(path + ".role", Message());
            if (field.Min?.Length > 64) fail(path + ".min", Message());
            if (field.Max?.Length > 64) fail(path + ".max", Message());
            if (field.MaxLength is < 1 or > 300) fail(path + ".maxLength", Message());
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}\\z", RegexOptions.CultureInvariant)] private static partial Regex FieldKeyPattern();
    public static string InvalidMessage() => ErrorMessages.Get(FormCatalogErrors.ContentInvalid.Code);
    private static string Message() => InvalidMessage();
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}\\z", RegexOptions.CultureInvariant)] private static partial Regex LocalIdPattern();
}
