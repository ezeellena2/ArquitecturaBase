using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// What the model reads of the agent (Casos del asistente): the profile, the active own cases in their order, «Cualquier otra cosa»,
/// the limits and the allowed actions, in the conversation's language. The fixed texts are not there: the server sends them.
/// </summary>
internal static class WhatsAppAssistantAgentViewPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] Cultures = ["es", "en"];

    /// <summary>The budget of one read of the engine, with room for its envelope.</summary>
    public const int ViewBytes = 15000;

    public static WhatsAppAssistantAgentPayload Agent(WhatsAppAssistantAgentRow agent, string culture)
    {
        var order = 0;
        var cases = agent.Cases.Where(item => item.IsActive).Select(item => new WhatsAppAssistantCaseView(item.Id, ++order, item.When, Input(item.InputType),
            Action(item.Action.Kind), item.Action.FormDefinitionId, item.Action.Instructions, Buttons(item.Action, culture, agent.OriginalCulture))).ToArray();
        return new(agent.Profile.Name, agent.Profile.Introduction, agent.Profile.Tone, agent.Profile.Does, agent.Profile.DoesNot, cases,
            new(Action(agent.Fallback.Kind), agent.Fallback.FormDefinitionId, agent.Fallback.Instructions, Buttons(agent.Fallback, culture, agent.OriginalCulture)),
            agent.Limits.BlockedTopics, agent.Limits.CannotPromise, agent.Limits.MaxReplyLength, [.. agent.Limits.AllowedActions.Select(Action)]);
    }

    /// <summary>Whether the model's view of the agent fits one read: the save says so before it can fail at a conversation.</summary>
    public static bool Fits(WhatsAppAssistantAgentRow agent) =>
        Cultures.All(culture => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { state = "complete", payload = Agent(agent, culture) }, JsonOptions)) <= ViewBytes);

    /// <summary>The confirmed facts in their order, up to the budget of one read.</summary>
    public static WhatsAppAssistantKnowledgePayload Knowledge(BusinessKnowledge knowledge)
    {
        var facts = new List<WhatsAppAssistantFactView>();
        var truncated = false;
        foreach (var section in knowledge.Sections)
            foreach (var fact in section.Facts)
            {
                var next = new WhatsAppAssistantKnowledgePayload(true, [.. facts, new(fact.Id, section.Key.ToString(), fact.Label, fact.Text)], false);
                if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { state = "complete", payload = next }, JsonOptions)) > ViewBytes)
                {
                    truncated = true;
                    break;
                }

                facts.Add(new(fact.Id, section.Key.ToString(), fact.Label, fact.Text));
            }

        return new(knowledge.Availability == BusinessKnowledgeAvailability.Available, facts, truncated);
    }

    public static string Input(WhatsAppAssistantInputType input) => input switch
    {
        WhatsAppAssistantInputType.Text => "text",
        WhatsAppAssistantInputType.Audio => "audio",
        _ => "any",
    };

    public static string Action(WhatsAppAssistantActionKind kind) => kind switch
    {
        WhatsAppAssistantActionKind.SendForm => "sendForm",
        WhatsAppAssistantActionKind.ExactText => "exactText",
        WhatsAppAssistantActionKind.DraftedReply => "draftedReply",
        WhatsAppAssistantActionKind.AskButtons => "askButtons",
        WhatsAppAssistantActionKind.Handoff => "handoff",
        _ => "noReply",
    };

    public static IReadOnlyList<WhatsAppAssistantCaseButtonView>? Buttons(WhatsAppAssistantAction action, string culture, string originalCulture) =>
        action.Kind != WhatsAppAssistantActionKind.AskButtons ? null : [.. (action.Buttons ?? []).Select(button => new WhatsAppAssistantCaseButtonView(
            button.Title.For(culture, originalCulture) ?? "", button.Destination switch
            {
                WhatsAppAssistantButtonDestination.SendForm => "sendForm",
                WhatsAppAssistantButtonDestination.ExactText => "exactText",
                _ => "handoff",
            }, button.FormDefinitionId))];
}
