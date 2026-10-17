using ArquitecturaBase.Application.Common.Logging;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Assistant;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantExecutionService(AiAssistantContextPolicy contexts, AiAssistantAccessGuard guard,
    AiAssistantCapabilityCatalog catalog, IAssistantCapabilityExecutor capabilities, ILlmConversationClient client,
    AiAssistantLimits limits, TimeProvider timeProvider, ILogger<AiAssistantExecutionService> logger) : IAiAssistantExecutionService
{
    private static readonly Meter _meter = new("ArquitecturaBase.AiAssistant", "1.0");
    private static readonly Histogram<double> _toolLatency = _meter.CreateHistogram<double>("ai.assistant.tool.duration", "s");
    public Task<Result<AiAssistantExecutionContextResponse>> PrepareAsync(AiAssistantClaimResponse claim, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantExecutionContextResponse>>(logger, "PrepareAssistant", () => contexts.PrepareAsync(claim, cancellationToken));

    public Task<Result<LlmConversationResponse>> CallProviderAsync(AiAssistantExecutionContextResponse context, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<LlmConversationResponse>>(logger, "CallProviderAsync", async () =>
        {
            var remaining = context.Claim.DeadlineAtUtc - timeProvider.GetUtcNow().UtcDateTime;
            if (remaining <= TimeSpan.Zero) return AiAssistantErrors.Expired;
            if (RequestBytes(context.ProviderRequest) > limits.ContextBytes) return AiAssistantErrors.LimitExceeded;
            using var timeout = new CancellationTokenSource(remaining < TimeSpan.FromSeconds(limits.ProviderTimeoutSeconds) ? remaining : TimeSpan.FromSeconds(limits.ProviderTimeoutSeconds), timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            return await client.SendAsync(context.ProviderRequest, linked.Token);
        });

    public Task<Result<AiAssistantAdvanceResponse>> AdvanceAsync(AiAssistantExecutionContextResponse context, LlmConversationResponse response, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantAdvanceResponse>>(logger, "AdvanceAsync", async () =>
        {
            if (response.State == "incomplete") return AiAssistantErrors.ProviderIncomplete;
            if (response.State == "refusal") return AiAssistantErrors.ProviderRejected;
            if (response.State != "tools" || response.ToolCalls.Count == 0) return AiAssistantErrors.InvalidOutput;
            if (response.Continuation is { } continuation && Encoding.UTF8.GetByteCount(continuation) > limits.ContinuationBytes) return AiAssistantErrors.LimitExceeded;
            var uniqueIds = response.ToolCalls.Select(t => t.CallId).ToHashSet(StringComparer.Ordinal);
            if (uniqueIds.Count != response.ToolCalls.Count || response.ToolCalls.Any(t => string.IsNullOrWhiteSpace(t.CallId) || t.CallId.Length > 200 || context.ObservedTools.Any(p => p.CallId == t.CallId)))
                return AiAssistantErrors.InvalidOutput;
            var profile = context.ExecutionProfile;
            if (profile is null || profile.ChannelContext.ActorUserId != context.Claim.OwnerUserId ||
                profile.ChannelContext.ChannelId != context.Turn.ChannelId) return AiAssistantErrors.ContentBlocked;
            var actor = await guard.AuthorizeCapturedAsync(profile.ChannelContext, cancellationToken);
            if (actor.IsFailure) return actor.Error;
            var terminals = response.ToolCalls.Where(t => t.Name is "assistant_answer" or "assistant_proposal").ToArray();
            if (terminals.Length != 0)
            {
                if (terminals.Length != 1 || response.ToolCalls.Count != 1 ||
                    !context.ProviderRequest.Tools.Any(t => t.Name == terminals[0].Name)) return AiAssistantErrors.InvalidOutput;
                if (terminals[0].Name == "assistant_proposal")
                {
                    var proposal = AiAssistantTerminalPolicy.ValidateProposal(profile.ChannelContext, context.Claim.TurnId,
                        terminals[0].Name, terminals[0].ArgumentsJson, context.Proposals);
                    return proposal.IsFailure ? proposal.Error : new AiAssistantAdvanceResponse(context, null, [], response.Usage) { Proposal = proposal.Value };
                }
                if (!profile.ChannelContext.AllowedTerminals.Contains(new AssistantTerminalReference("assistant_answer", 1))) return AiAssistantErrors.InvalidOutput;
                var answer = ParseAnswer(terminals[0].ArgumentsJson);
                if (answer.IsFailure) return answer.Error;
                var verified = AiAssistantEvidenceVerifier.Verify(answer.Value, context.Evidences);
                return verified.IsFailure ? verified.Error : new AiAssistantAdvanceResponse(context, answer.Value, [], response.Usage);
            }
            if (context.ToolCalls + response.ToolCalls.Count > limits.ToolCalls) return AiAssistantErrors.LimitExceeded;
            var evidences = context.Evidences.ToList();
            var observed = context.ObservedTools.ToList();
            var proposals = context.Proposals.ToList();
            var reads = context.ObservedReads.ToList();
            var fresh = new List<AiAssistantObservedTool>();
            var results = new List<LlmToolResult>();
            var bytes = context.ToolBytes;
            var culture = CultureInfo.GetCultureInfo(context.Turn.Culture);
            var previousCulture = CultureInfo.CurrentCulture;
            var previousUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                foreach (var call in response.ToolCalls)
                {
                    var reference = profile.ChannelContext.AllowedCapabilities.SingleOrDefault(c => c.Id == call.Name);
                    var definition = reference is null ? null : catalog.Find(reference);
                    if (definition is null || !context.ProviderRequest.Tools.Any(t => t.Name == call.Name)) return AiAssistantErrors.ToolRejected;
                    var channel = profile.ChannelContext with { RequiredPermissions = definition.RequiredPermissions };
                    if ((await guard.AuthorizeAsync(channel, cancellationToken)).IsFailure) return AiAssistantErrors.ToolRejected;
                    if (Encoding.UTF8.GetByteCount(call.ArgumentsJson) > definition.Budget.InputBytes) return AiAssistantErrors.LimitExceeded;
                    // The provider has not observed this batch's fresh reads yet. Only prior confirmed steps may authorize a proposal.
                    if (context.ObservedReads.Any(r => r.TurnId != context.Claim.TurnId)) return AiAssistantErrors.ToolRejected;
                    var request = new AssistantCapabilityRequest(context.Claim.TurnId, profile.ChannelContext, reference!, call.ArgumentsJson, context.ObservedReads);
                    var remaining = context.Claim.DeadlineAtUtc - timeProvider.GetUtcNow().UtcDateTime;
                    if (remaining <= TimeSpan.Zero) return AiAssistantErrors.Expired;
                    var timeoutLimit = TimeSpan.FromSeconds(Math.Min(limits.ToolTimeoutSeconds, definition.Budget.TimeoutSeconds));
                    using var timeout = new CancellationTokenSource(remaining < timeoutLimit ? remaining : timeoutLimit, timeProvider);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                    var started = timeProvider.GetTimestamp();
                    Result<AssistantCapabilityResponse> result;
                    try { result = await capabilities.ExecuteAsync(request, linked.Token); }
                    finally { _toolLatency.Record(timeProvider.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("tool", definition.Id)); }
                    linked.Token.ThrowIfCancellationRequested();
                    if (result.IsFailure) return result.Error;
                    if (result.Value.ItemCount < 0 || result.Value.ItemCount > definition.Budget.MaximumItems) return AiAssistantErrors.LimitExceeded;
                    var payloadJson = JsonSerializer.Serialize(result.Value.Payload, result.Value.Payload.GetType(), AiAssistantResponsePolicy.JsonOptions);
                    if (catalog.ParseOutput(reference!, payloadJson).IsFailure) return AiAssistantErrors.InvalidOutput;
                    var payload = JsonSerializer.SerializeToElement(result.Value.Payload, result.Value.Payload.GetType(), AiAssistantResponsePolicy.JsonOptions);
                    var proposalId = definition.Kind == AssistantCapabilityKind.Proposal ? Guid.NewGuid() : (Guid?)null;
                    // Home retains its existing state/evidence bytes; other owners expose only their typed payload.
                    var json = result.Value.Payload is AssistantSystemQueryPayload ? payloadJson : proposalId is { } id
                        ? JsonSerializer.Serialize(new { result.Value.State, proposalId = id, payload }, AiAssistantResponsePolicy.JsonOptions)
                        : JsonSerializer.Serialize(new { result.Value.State, payload }, AiAssistantResponsePolicy.JsonOptions);
                    var resultBytes = Encoding.UTF8.GetByteCount(json);
                    if (resultBytes > Math.Min(limits.ToolResultBytes, definition.Budget.OutputBytes) || bytes + resultBytes > limits.ToolAggregateBytes) return AiAssistantErrors.LimitExceeded;
                    foreach (var evidence in result.Value.Evidences)
                    {
                        if (evidence.SourceId != call.Name || evidence.SourceVersion != definition.Version || evidence.Id == Guid.Empty ||
                            evidence.RequiredPermissions.Except(definition.RequiredPermissions.Actor, StringComparer.Ordinal).Any()) return AiAssistantErrors.InvalidOutput;
                        evidences.Add(evidence);
                    }
                    bytes += resultBytes;
                    results.Add(new(call.CallId, call.Name, json));
                    var ordinal = context.ToolCalls + results.Count;
                    var tool = new AiAssistantObservedTool(ordinal, call.CallId, call.Name, definition.Version,
                        definition.RequiredPermissions.Actor, (result.Value.Evidences.Count == 0 ? null : result.Value.Evidences[0]), json, resultBytes)
                    { State = result.Value.State, ObservedAtUtc = timeProvider.GetUtcNow().UtcDateTime, Kind = definition.Kind,
                        ProposalId = proposalId, ReadOrdinals = context.ObservedReads.Select(r => r.Ordinal).ToArray() };
                    fresh.Add(tool);
                    observed.Add(tool);
                    if (proposalId is { } generatedId)
                        proposals.Add(new(context.Claim.TurnId, generatedId, reference!, result.Value.Payload) { ObservedReads = context.ObservedReads });
                    else reads.Add(new(context.Claim.TurnId, ordinal, reference!, result.Value.Payload));
                }
            }
            finally { CultureInfo.CurrentCulture = previousCulture; CultureInfo.CurrentUICulture = previousUiCulture; }
            var nextRequest = context.ProviderRequest with { Continuation = response.Continuation, ToolResults = results };
            var next = context with { ProviderRequest = nextRequest, Evidences = evidences, ObservedTools = observed, Proposals = proposals,
                ObservedReads = reads, ToolCalls = context.ToolCalls + response.ToolCalls.Count, ToolBytes = bytes };
            return new AiAssistantAdvanceResponse(next, null, fresh, response.Usage);
        });

    private static Result<AiAssistantAnswer> ParseAnswer(string arguments)
    {
        try
        {
            using var json = JsonDocument.Parse(arguments);
            if (!StrictObject(json.RootElement, ["kind", "text", "evidenceIds"], 3)) return AiAssistantErrors.InvalidOutput;
            var root = json.RootElement;
            if (root.GetProperty("kind").ValueKind != JsonValueKind.String || root.GetProperty("text").ValueKind != JsonValueKind.String ||
                root.GetProperty("evidenceIds").ValueKind != JsonValueKind.Array) return AiAssistantErrors.InvalidOutput;
            var ids = new List<Guid>();
            foreach (var id in root.GetProperty("evidenceIds").EnumerateArray())
                if (id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var value)) return AiAssistantErrors.InvalidOutput;
                else ids.Add(value);
            return new AiAssistantAnswer(root.GetProperty("kind").GetString()!, root.GetProperty("text").GetString()!, ids);
        }
        catch (JsonException) { return AiAssistantErrors.InvalidOutput; }
    }


    private static bool StrictObject(JsonElement element, string[] allowed, int? expectedCount)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var properties = element.EnumerateObject().Select(p => p.Name).ToArray();
        return properties.Distinct(StringComparer.Ordinal).Count() == properties.Length &&
            (expectedCount is null || properties.Length == expectedCount) && properties.All(p => allowed.Contains(p, StringComparer.Ordinal));
    }
    private static int RequestBytes(LlmConversationRequest request) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
        new { request.Model, request.SystemPrompt, request.Messages, request.Tools, request.ToolResults, request.Continuation }, AiAssistantResponsePolicy.JsonOptions));
}
