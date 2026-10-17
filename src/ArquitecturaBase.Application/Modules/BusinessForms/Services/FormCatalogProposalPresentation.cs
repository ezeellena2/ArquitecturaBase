using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogProposalPresentation
{
    public static FormCatalogProposalEvidence Evidence(string json) => JsonSerializer.Deserialize<FormCatalogProposalEvidence>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog proposal evidence is invalid.");

    public static async Task<FormCatalogProposalResponse?> ReadAsync(string? evidenceJson, BusinessFormCatalogProposalState? state,
        Guid? decidedBy, DateTime? decidedAtUtc, IUserReader users, CancellationToken ct)
    {
        if (evidenceJson is null) return null;
        var evidence = Evidence(evidenceJson);
        var requester = await users.FindByIdAsync(evidence.RequestedByUserId, ct);
        var decider = decidedBy is { } actor ? await users.FindByIdAsync(actor, ct) : null;
        return new(state!.Value.ToString(), evidence.OriginalCulture, new(evidence.RequestedByUserId, requester?.DisplayName),
            evidence.RequestedAtUtc, decidedBy is { } id ? new(id, decider?.DisplayName) : null, decidedAtUtc,
            evidence.Provenance, evidence.OriginalProposal);
    }

    public static async Task<FormCatalogDetailResponse> MapAsync(BusinessFormCatalog catalog, int? usageCount, IUserReader users, CancellationToken ct) =>
        new(catalog.Id, catalog.Key, Texts(catalog.NameJson), Texts(catalog.DescriptionJson), catalog.Kind.ToString(), catalog.Revision,
            catalog.IsActive, 0, usageCount, null, catalog.FiltersJson is null ? null : JsonSerializer.Deserialize<FormCatalogFilterDefinition>(catalog.FiltersJson, JsonSerializerOptions.Web),
            await ReadAsync(catalog.ProposalEvidenceJson, catalog.ProposalState, catalog.ProposalDecidedByUserId, catalog.ProposalDecidedAtUtc, users, ct));

    private static Dictionary<string,string> Texts(string json) => JsonSerializer.Deserialize<Dictionary<string,string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog texts are invalid.");
}
