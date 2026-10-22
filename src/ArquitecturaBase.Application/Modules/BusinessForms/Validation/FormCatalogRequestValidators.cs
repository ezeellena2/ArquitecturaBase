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
        CheckMap(name, "name", 120, fail);
        CheckMap(description, "description", 1000, fail);
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

    private static void CheckMap(IReadOnlyDictionary<string, string?>? values, string path, int maxLength,
        Action<string, string> fail)
    {
        if (values is null)
        {
            fail(path, InvalidMessage);
            return;
        }

        foreach (var (culture, value) in values)
            if (culture is not ("es" or "en") || string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
                fail(path + "." + culture, InvalidMessage);
        foreach (var culture in new[] { "es", "en" })
            if (!values.TryGetValue(culture, out var value) || string.IsNullOrWhiteSpace(value))
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
        void CheckMap(IReadOnlyDictionary<string, string?>? map, string path, int maxLength, bool required, bool allowNulls = false,
            params string[] requiredCultures)
        {
            if (map is null) { if (required) fail(path, Message()); return; }
            foreach (var (language, value) in map)
                if (language is not ("es" or "en") || (!allowNulls && value is null) || (value?.Length > maxLength)
                    || (requiredCultures.Contains(language, StringComparer.Ordinal) && string.IsNullOrWhiteSpace(value))) fail(path + "." + language, Message());
            foreach (var language in requiredCultures)
                    if (!map.TryGetValue(language, out var value) || string.IsNullOrWhiteSpace(value)) fail(path + "." + language, Message());
        }

        CheckMap(name, "name", 120, required: true, requiredCultures: ["es", "en"]);
        CheckMap(description, "description", 1000, required: true, requiredCultures: ["es", "en"]);
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
            CheckMap(item.Text, path + ".text", BusinessFormCatalogItem.MaxTextLength, required: true, allowNulls: true, requiredCultures: ["es"]);
            CheckMap(item.Detail, path + ".detail", BusinessFormCatalogItem.MaxDetailLength, required: false, allowNulls: true);
        }
    }

    public static string InvalidMessage() => ErrorMessages.Get(FormCatalogErrors.ContentInvalid.Code);
    private static string Message() => InvalidMessage();
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}\\z", RegexOptions.CultureInvariant)] private static partial Regex LocalIdPattern();
}
