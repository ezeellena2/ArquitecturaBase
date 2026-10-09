using ArquitecturaBase.Application.Common.Logging;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.SystemQueries;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantExecutionService(AiAssistantContextPolicy contexts, AiAssistantAccessGuard guard, IEnumerable<ISystemQuerySource> sources, ISystemQueryExecutor queries, ILlmConversationClient client, AiAssistantLimits limits, TimeProvider timeProvider, ILogger<AiAssistantExecutionService> logger) : IAiAssistantExecutionService
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
            var terminal = response.ToolCalls.Where(t => t.Name == "assistant_answer").ToArray();
            if (terminal.Length != 0)
            {
                if (terminal.Length != 1 || response.ToolCalls.Count != 1) return AiAssistantErrors.InvalidOutput;
                var answer = ParseAnswer(terminal[0].ArgumentsJson);
                if (answer.IsFailure) return answer.Error;
                var verified = AiAssistantEvidenceVerifier.Verify(answer.Value, context.Evidences);
                return verified.IsFailure ? verified.Error : new AiAssistantAdvanceResponse(context, answer.Value, [], response.Usage);
            }
            if (context.ToolCalls + response.ToolCalls.Count > limits.ToolCalls) return AiAssistantErrors.LimitExceeded;
            var actor = await guard.AuthorizeAsync(context.Claim.OwnerUserId, cancellationToken);
            if (actor.IsFailure) return actor.Error;
            var evidences = context.Evidences.ToList();
            var observed = context.ObservedTools.ToList();
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
                    var source = sources.SingleOrDefault(s => s.Definitions.Any(d => d.Id == call.Name));
                    var definition = source?.Definitions.Single(d => d.Id == call.Name);
                    if (definition is null || !context.ProviderRequest.Tools.Any(t => t.Name == call.Name) ||
                        !await guard.CanReadAsync(context.Claim.OwnerUserId, definition.RequiredPermissions, cancellationToken)) return AiAssistantErrors.ToolRejected;
                    var request = ParseQuery(context, call);
                    if (request.IsFailure) return request.Error;
                    var remaining = context.Claim.DeadlineAtUtc - timeProvider.GetUtcNow().UtcDateTime;
                    if (remaining <= TimeSpan.Zero) return AiAssistantErrors.Expired;
                    using var timeout = new CancellationTokenSource(remaining < TimeSpan.FromSeconds(limits.ToolTimeoutSeconds) ? remaining : TimeSpan.FromSeconds(limits.ToolTimeoutSeconds), timeProvider);
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                    var started = timeProvider.GetTimestamp();
                    Result<SystemQueryResponse> query;
                    try { query = await queries.ExecuteAsync(request.Value, linked.Token); }
                    finally { _toolLatency.Record(timeProvider.GetElapsedTime(started).TotalSeconds, new KeyValuePair<string, object?>("tool", definition.Id)); }
                    if (query.IsFailure) return query.Error;
                    var json = JsonSerializer.Serialize(query.Value, AiAssistantResponsePolicy.JsonOptions);
                    var resultBytes = Encoding.UTF8.GetByteCount(json);
                    if (resultBytes > Math.Min(limits.ToolResultBytes, definition.MaximumBytes) || bytes + resultBytes > limits.ToolAggregateBytes ||
                        query.Value.Evidence?.Rows.Count > definition.MaximumRows) return AiAssistantErrors.LimitExceeded;
                    bytes += resultBytes;
                    results.Add(new(call.CallId, call.Name, json));
                    if (query.Value.Evidence is { } evidence)
                    {
                        if (evidence.SourceId != call.Name || evidence.SourceVersion != definition.Version || evidence.Id == Guid.Empty ||
                            evidence.RequiredPermissions.Except(definition.RequiredPermissions, StringComparer.Ordinal).Any()) return AiAssistantErrors.InvalidOutput;
                        evidences.Add(evidence);
                    }
                    var tool = new AiAssistantObservedTool(context.ToolCalls + results.Count, call.CallId, call.Name, definition.Version,
                        definition.RequiredPermissions, query.Value.Evidence, json, resultBytes)
                    { State = query.Value.State, ObservedAtUtc = timeProvider.GetUtcNow().UtcDateTime };
                    fresh.Add(tool);
                    observed.Add(tool);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
            var nextRequest = context.ProviderRequest with { Continuation = response.Continuation, ToolResults = results };
            var next = context with
            {
                ProviderRequest = nextRequest,
                Evidences = evidences,
                ObservedTools = observed,
                ToolCalls = context.ToolCalls + response.ToolCalls.Count,
                ToolBytes = bytes
            };
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

    private static Result<SystemQueryRequest> ParseQuery(AiAssistantExecutionContextResponse context, LlmToolCall call)
    {
        try
        {
            using var json = JsonDocument.Parse(call.ArgumentsJson);
            var root = json.RootElement;
            Guid? roleId = null;
            string? roleName = null;
            string? helpTopic = null;
            if (call.Name == "roles.permissions")
            {
                if (!StrictObject(root, ["roleId", "roleName"], null)) return AiAssistantErrors.ToolRejected;
                if (root.TryGetProperty("roleId", out var id) && id.ValueKind != JsonValueKind.Null)
                {
                    if (id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var value) || value == Guid.Empty) return AiAssistantErrors.ToolRejected;
                    roleId = value;
                }
                if (root.TryGetProperty("roleName", out var name) && name.ValueKind != JsonValueKind.Null)
                {
                    if (name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 256) return AiAssistantErrors.ToolRejected;
                    roleName = name.GetString();
                }
                if ((roleId is null) == (roleName is null)) return AiAssistantErrors.ToolRejected;
            }
            else if (call.Name == "system.help")
            {
                if (!StrictObject(root, ["topic"], 1) || !root.TryGetProperty("topic", out var topic) || topic.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(topic.GetString()) || topic.GetString()!.Length > 100) return AiAssistantErrors.ToolRejected;
                helpTopic = topic.GetString();
            }
            else if (!StrictObject(root, [], 0)) return AiAssistantErrors.ToolRejected;
            return new SystemQueryRequest(context.Claim.OwnerUserId, call.Name, context.Turn.Culture, context.Turn.TimeZoneId, roleId, roleName, helpTopic);
        }
        catch (JsonException) { return AiAssistantErrors.ToolRejected; }
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
