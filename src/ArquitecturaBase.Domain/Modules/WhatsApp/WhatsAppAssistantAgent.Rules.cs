using ArquitecturaBase.Domain.Common;
using static ArquitecturaBase.Domain.Modules.WhatsApp.WhatsAppAssistantAgentLimits;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed partial class WhatsAppAssistantAgent
{
    /// <summary>
    /// The invariants of a save: lengths and counts from <see cref="WhatsAppAssistantAgentLimits"/>, the original language of
    /// each fixed text, the closed menu and the actions the agent may use. The validator says which field; this is the last word.
    /// </summary>
    private sealed class Rules(string originalCulture)
    {
        public bool Valid { get; private set; } = true;

        public (WhatsAppAssistantProfile Profile, IReadOnlyList<WhatsAppAssistantCase> Cases, WhatsAppAssistantAction Fallback,
            WhatsAppAssistantLimits Limits) Normalize(WhatsAppAssistantProfile? profile, IReadOnlyList<WhatsAppAssistantCase>? cases,
            WhatsAppAssistantAction? fallback, WhatsAppAssistantLimits? limits)
        {
            if (profile is null || cases is null || fallback is null || limits is null)
            {
                Valid = false;
                return default;
            }

            var allowed = Allowed(limits.AllowedActions);
            var normalizedLimits = new WhatsAppAssistantLimits(Topics(limits.BlockedTopics), Text(limits.CannotPromise, CannotPromiseLength, false),
                Range(limits.MaxReplyLength, MinReplyLength, MaxReplyLength), Range(limits.DailyReplyLimit, MinDailyReplies, MaxDailyReplies), allowed);
            var normalizedProfile = new WhatsAppAssistantProfile(Text(profile.Name, NameLength, true), Text(profile.Introduction, IntroductionLength, false),
                Text(profile.Tone, ToneLength, false), Text(profile.Does, DoesLength, false), Text(profile.DoesNot, DoesLength, false),
                Fixed(profile.HandoffMessage, HandoffMessageLength) ?? new(null, null));
            Require(cases.Count <= WhatsAppAssistantAgentLimits.Cases);
            var ids = new HashSet<Guid>();
            var normalizedCases = cases.Select(item => Case(item, ids, allowed)).ToArray();
            return (normalizedProfile, normalizedCases, Action(fallback, allowed), normalizedLimits);
        }

        private WhatsAppAssistantCase Case(WhatsAppAssistantCase? item, HashSet<Guid> ids, IReadOnlyList<WhatsAppAssistantActionKind> allowed)
        {
            if (item is null)
            {
                Valid = false;
                return new(Guid.Empty, "", WhatsAppAssistantInputType.Any, false, new(WhatsAppAssistantActionKind.NoReply));
            }

            Require(item.Id != Guid.Empty && ids.Add(item.Id) && Enum.IsDefined(item.InputType));
            return new(item.Id, Text(item.When, WhenLength, true), item.InputType, item.IsActive, Action(item.Action, allowed));
        }

        private WhatsAppAssistantAction Action(WhatsAppAssistantAction? action, IReadOnlyList<WhatsAppAssistantActionKind> allowed)
        {
            if (action is null || !Enum.IsDefined(action.Kind) || !allowed.Contains(action.Kind))
            {
                Valid = false;
                return new(WhatsAppAssistantActionKind.NoReply);
            }

            var buttons = action.Buttons ?? [];
            switch (action.Kind)
            {
                case WhatsAppAssistantActionKind.SendForm:
                    Require(action.FormDefinitionId is { } form && form != Guid.Empty && Absent(action.Text, action.Question)
                        && Blank(action.Instructions, action.HandoffReason) && buttons.Count == 0);
                    return new(action.Kind, FormDefinitionId: action.FormDefinitionId);
                case WhatsAppAssistantActionKind.ExactText:
                    Require(action.FormDefinitionId is null && Absent(action.Question) && Blank(action.Instructions, action.HandoffReason) && buttons.Count == 0);
                    return new(action.Kind, Text: Fixed(action.Text, ExactTextLength));
                case WhatsAppAssistantActionKind.DraftedReply:
                    Require(action.FormDefinitionId is null && Absent(action.Text, action.Question) && Blank(action.HandoffReason) && buttons.Count == 0);
                    return new(action.Kind, Instructions: Optional(action.Instructions, InstructionsLength));
                case WhatsAppAssistantActionKind.AskButtons:
                    Require(action.FormDefinitionId is null && Absent(action.Text) && Blank(action.Instructions, action.HandoffReason));
                    return new(action.Kind, Question: Fixed(action.Question, QuestionLength), Buttons: Buttons(buttons, allowed));
                case WhatsAppAssistantActionKind.Handoff:
                    Require(action.FormDefinitionId is null && Absent(action.Text, action.Question) && Blank(action.Instructions) && buttons.Count == 0);
                    return new(action.Kind, HandoffReason: Text(action.HandoffReason, HandoffReasonLength, true));
                default:
                    Require(action.FormDefinitionId is null && Absent(action.Text, action.Question) && Blank(action.Instructions, action.HandoffReason) && buttons.Count == 0);
                    return new(action.Kind);
            }
        }

        private WhatsAppAssistantButton[] Buttons(IReadOnlyList<WhatsAppAssistantButton> buttons, IReadOnlyList<WhatsAppAssistantActionKind> allowed)
        {
            Require(buttons.Count is >= 1 and <= WhatsAppAssistantAgentLimits.Buttons);
            var normalized = buttons.Select(button => Button(button, allowed)).ToArray();
            // WhatsApp needs distinct titles: compared as the person sees them in each language, where a title without its
            // translation goes out in the original (WhatsAppAssistantText.For).
            foreach (var culture in new[] { "es", "en" })
            {
                var titles = normalized.Select(button => button.Title.For(culture, originalCulture)).OfType<string>().ToArray();
                Require(titles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == titles.Length);
            }

            return normalized;
        }

        private WhatsAppAssistantButton Button(WhatsAppAssistantButton? button, IReadOnlyList<WhatsAppAssistantActionKind> allowed)
        {
            if (button is null || !Enum.IsDefined(button.Destination))
            {
                Valid = false;
                return new(new(null, null), WhatsAppAssistantButtonDestination.Handoff, null, null);
            }

            var title = Fixed(button.Title, ButtonTitleLength) ?? new(null, null);
            var kind = button.Destination switch
            {
                WhatsAppAssistantButtonDestination.SendForm => WhatsAppAssistantActionKind.SendForm,
                WhatsAppAssistantButtonDestination.ExactText => WhatsAppAssistantActionKind.ExactText,
                _ => WhatsAppAssistantActionKind.Handoff,
            };
            Require(allowed.Contains(kind));
            return button.Destination switch
            {
                WhatsAppAssistantButtonDestination.SendForm => Checked(button.FormDefinitionId is { } form && form != Guid.Empty && button.Text is null,
                    new WhatsAppAssistantButton(title, button.Destination, button.FormDefinitionId, null)),
                WhatsAppAssistantButtonDestination.ExactText => Checked(button.FormDefinitionId is null,
                    new WhatsAppAssistantButton(title, button.Destination, null, Fixed(button.Text, ExactTextLength))),
                _ => Checked(button.FormDefinitionId is null && button.Text is null, new WhatsAppAssistantButton(title, button.Destination, null, null)),
            };
        }

        private List<WhatsAppAssistantActionKind> Allowed(IReadOnlyList<WhatsAppAssistantActionKind>? actions)
        {
            var values = actions ?? [];
            Require(values.Count > 0 && values.All(Enum.IsDefined) && values.Distinct().Count() == values.Count);
            return [.. values.Distinct().Order()];
        }

        private List<string> Topics(IReadOnlyList<string>? topics)
        {
            var values = (topics ?? []).Select(topic => Text(topic, BlockedTopicLength, true)).ToList();
            Require(values.Count <= BlockedTopics && values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Count);
            return values;
        }

        /// <summary>A fixed text needs its original language; the translation is optional.</summary>
        private WhatsAppAssistantText? Fixed(WhatsAppAssistantText? text, int maximumLength)
        {
            if (text is null)
            {
                Valid = false;
                return null;
            }

            var normalized = new WhatsAppAssistantText(Optional(text.Es, maximumLength), Optional(text.En, maximumLength));
            Require(normalized.In(originalCulture) is not null);
            return normalized;
        }

        private string Text(string? value, int maximumLength, bool required)
        {
            var text = Optional(value, maximumLength);
            Require(!required || text is not null);
            return text ?? "";
        }

        private string? Optional(string? value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();
            Require(text.Length <= maximumLength && StorableText.IsStorable(text));
            return text;
        }

        private int Range(int value, int minimum, int maximum)
        {
            Require(value >= minimum && value <= maximum);
            return value;
        }

        private T Checked<T>(bool condition, T value)
        {
            Require(condition);
            return value;
        }

        private void Require(bool condition)
        {
            if (!condition) Valid = false;
        }

        private static bool Absent(params WhatsAppAssistantText?[] texts) => texts.All(text => text is null);

        private static bool Blank(params string?[] values) => values.All(string.IsNullOrWhiteSpace);
    }
}
