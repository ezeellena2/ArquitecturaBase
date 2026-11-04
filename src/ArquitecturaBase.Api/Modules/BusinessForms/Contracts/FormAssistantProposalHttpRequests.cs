using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

/// <summary>The Home turn that brought the proposal, by its request, and the proposal to create.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateFromAssistantProposalHttpRequest(Guid? RequestId, Guid? ProposalId);
