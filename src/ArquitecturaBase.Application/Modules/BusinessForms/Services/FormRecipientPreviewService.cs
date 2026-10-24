using System.Globalization;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormRecipientPreviewService(FormDraftAccessGuard access, IRequestValidator validator,
    IFormCatalogFilterReader samples, IUserReader users, IFormPersonDataReader people,
    FormCatalogOptionsPolicy options, IFormDefinitionReader definitions) : IFormRecipientPreviewService
{
    public async Task<Result<PagedResult<FormCatalogFilterSampleUserResponse>>> ListSamplesAsync(ListFormRecipientSamplesRequest request, CancellationToken ct)
    {
        var allowed = await access.CheckAsync(true, ct);
        if (allowed.IsFailure) return allowed.Error;
        if (await validator.ValidateAsync(request, ct) is { } invalid) return invalid;
        var page = await samples.ListSampleUsersAsync(new() { Page = request.Page, PageSize = request.PageSize, Search = request.Search, Sort = request.Sort }, ct);
        allowed = await access.CheckAsync(true, ct);
        return allowed.IsFailure ? allowed.Error : page;
    }

    public async Task<Result<FormRecipientPreviewResponse>> PreviewAsync(Guid definitionId, FormRecipientPreviewRequest request, CancellationToken ct)
    {
        var allowed = await access.CheckAsync(true, ct);
        if (allowed.IsFailure) return allowed.Error;
        if (await validator.ValidateAsync(request, ct) is { } invalid) return invalid;
        var definition = await definitions.FindAsync(definitionId, ct);
        if (definition is null) return await MissingAsync(FormDefinitionErrors.NotFound, ct);
        if (!await samples.ExistsActiveSampleUserAsync(request.SampleUserId, ct)) return await MissingAsync(FormPersonDataErrors.UserNotFound, ct);
        var user = await users.FindByIdAsync(request.SampleUserId, ct);
        if (user is null) return await MissingAsync(FormPersonDataErrors.UserNotFound, ct);
        var data = await people.FindRecipientDataAsync(request.SampleUserId, ct);
        var values = FormCatalogMatchPolicy.RecipientValues(data);
        var uiCulture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
        var matchedAttributes = (request.Source.Match ?? []).Where(match => match.PersonAttributeId is not null)
            .Select(match => match.PersonAttributeId!.Value).ToHashSet();
        var recipient = new FormRecipientPreviewRecipientResponse(request.SampleUserId, user.DisplayName ?? "", data.Attributes
            .Where(attribute => matchedAttributes.Contains(attribute.Id) && attribute.IsActive && values.ContainsKey(attribute.Id.ToString("D")))
            .Select(attribute => new FormRecipientPreviewAttributeResponse(attribute.Id,
                FormRecordPresentationPolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(attribute.NameJson), uiCulture),
                attribute.Type == "Catalog" && Guid.TryParse(values[attribute.Id.ToString("D")], out var itemId)
                    ? data.OptionTexts.GetValueOrDefault(itemId)?.Text ?? "" : values[attribute.Id.ToString("D")])).ToArray());
        var field = new CanonicalFormField(Guid.Empty, "preview", request.Type,
            FormDraftPolicy.Read<Dictionary<string, string>>(definition.NameJson), null, false, null, null, request.Source,
            Selection: request.Type == "multiChoice" ? new(0, 20) : null);
        var prepared = await options.PrepareAsync(request.SampleUserId, request.Culture, new(1, [field]), definition.Culture, ct, inspectOnly: true, recipientData: data);
        var missing = prepared.Issues.Any(issue => issue.Code == FormRecordErrors.RecipientDataMissing.Code);
        var state = missing ? "RecipientDataMissing" : prepared.Issues.Count > 0 ? "FilterUnavailable" : "Ready";
        if (state == "FilterUnavailable") recipient = recipient with { Values = [] };
        var offered = prepared.Catalogs.Count > 0 ? prepared.Catalogs[0].Options : [];
        var limit = request.Type == "multiChoice" ? 20 : 200;
        var response = new FormRecipientPreviewResponse(recipient, state, offered.Count, limit, offered.Count > limit,
            offered.Take(limit).Select(option => new FormRecipientPreviewOptionResponse(option.Id, option.Shown?.Title ?? option.Text,
                option.Shown is null ? option.Detail : option.Shown.Description)).ToArray());
        allowed = await access.CheckAsync(true, ct);
        return allowed.IsFailure ? allowed.Error : response;
    }
    private async Task<Result<FormRecipientPreviewResponse>> MissingAsync(Error error, CancellationToken ct)
    {
        var allowed = await access.CheckAsync(true, ct);
        return allowed.IsFailure ? allowed.Error : error;
    }
}
