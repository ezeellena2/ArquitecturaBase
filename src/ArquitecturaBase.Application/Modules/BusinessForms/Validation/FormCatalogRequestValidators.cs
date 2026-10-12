using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

internal sealed class CreateManualCatalogRequestValidator : AbstractValidator<CreateManualCatalogRequest>
{
    public CreateManualCatalogRequestValidator() => RuleFor(request => request).Custom((request, context) =>
        FormCatalogRequestValidation.Validate(request.Name, request.Description, request.Items, context, creating: true));
}

internal sealed class UpdateManualCatalogRequestValidator : AbstractValidator<UpdateManualCatalogRequest>
{
    public UpdateManualCatalogRequestValidator() => RuleFor(request => request).Custom((request, context) =>
    {
        if (request.ExpectedRevision is null or < 1 or > 9007199254740991)
            context.AddFailure("expectedRevision", FormCatalogRequestValidation.InvalidMessage());
        FormCatalogRequestValidation.Validate(request.Name, request.Description, request.Items, context, creating: false);
    });
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
        if (items is null || items.Count > 500) { fail("items", Message()); return; }
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
            CheckMap(item.Text, path + ".text", 200, required: true, allowNulls: true, requiredCultures: ["es"]);
            CheckMap(item.Detail, path + ".detail", 500, required: false, allowNulls: true);
            if (item.Position is < 1 or > 10000) fail(path + ".position", Message());
        }
    }

    public static string InvalidMessage() => ErrorMessages.Get(FormCatalogErrors.ContentInvalid.Code);
    private static string Message() => InvalidMessage();
    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}\\z", RegexOptions.CultureInvariant)] private static partial Regex LocalIdPattern();
}
