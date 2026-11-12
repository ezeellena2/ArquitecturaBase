using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using FluentValidation;
using static ArquitecturaBase.Domain.Modules.WhatsApp.WhatsAppAssistantAgentLimits;
using Messages = ArquitecturaBase.Application.Modules.WhatsApp.Validation.WhatsAppAssistantAgentValidationMessages;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

/// <summary>
/// Says which field of the four tabs is wrong, with its path (<c>Cases[2].Action.Text</c>). The limits are the agent's
/// (<see cref="WhatsAppAssistantAgentLimits"/>); the entity checks them again and has the last word.
/// </summary>
internal sealed class UpdateWhatsAppAssistantAgentRequestValidator : AbstractValidator<UpdateWhatsAppAssistantAgentRequest>
{
    private static readonly string[] Languages = ["es", "en"];

    public UpdateWhatsAppAssistantAgentRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0).WithMessage(_ => Messages.Invalid);
        RuleFor(request => request.OriginalCulture).Must(culture => culture is "es" or "en").WithMessage(_ => ValidationMessages.CultureInvalid);
        RuleFor(request => request).Custom(Validate);
    }

    private static void Validate(UpdateWhatsAppAssistantAgentRequest request, ValidationContext<UpdateWhatsAppAssistantAgentRequest> context)
    {
        var culture = request.OriginalCulture is "en" ? "en" : "es";
        var allowed = request.Limits?.AllowedActions ?? [];
        if (request.Profile is not { } profile) context.AddFailure("Profile", ValidationMessages.Required);
        else
        {
            Text(context, "Profile.Name", profile.Name, NameLength, required: true);
            Text(context, "Profile.Introduction", profile.Introduction, IntroductionLength, required: false);
            Text(context, "Profile.Tone", profile.Tone, ToneLength, required: false);
            Text(context, "Profile.Does", profile.Does, DoesLength, required: false);
            Text(context, "Profile.DoesNot", profile.DoesNot, DoesLength, required: false);
            Fixed(context, "Profile.HandoffMessage", profile.HandoffMessage, HandoffMessageLength, culture);
        }

        if (request.Limits is not { } limits) context.AddFailure("Limits", ValidationMessages.Required);
        else Limits(context, limits);
        if (request.Cases is not { } cases) context.AddFailure("Cases", ValidationMessages.Required);
        else
        {
            if (cases.Count > WhatsAppAssistantAgentLimits.Cases) context.AddFailure("Cases", Messages.TooMany(WhatsAppAssistantAgentLimits.Cases));
            var ids = new HashSet<Guid>();
            for (var index = 0; index < cases.Count; index++)
            {
                var path = $"Cases[{index}]";
                if (cases[index] is not { } item)
                {
                    context.AddFailure(path, Messages.Invalid);
                    continue;
                }

                if (item.Id == Guid.Empty || !ids.Add(item.Id)) context.AddFailure(path + ".Id", Messages.Invalid);
                Text(context, path + ".When", item.When, WhenLength, required: true);
                if (!Enum.IsDefined(item.InputType)) context.AddFailure(path + ".InputType", Messages.Invalid);
                Action(context, path + ".Action", item.Action, allowed, culture);
            }
        }

        if (request.Fallback is null) context.AddFailure("Fallback", ValidationMessages.Required);
        else Action(context, "Fallback", request.Fallback, allowed, culture);
    }

    private static void Limits(ValidationContext<UpdateWhatsAppAssistantAgentRequest> context, WhatsAppAssistantLimits limits)
    {
        if (limits.BlockedTopics is not { } topics) context.AddFailure("Limits.BlockedTopics", ValidationMessages.Required);
        else
        {
            if (topics.Count > BlockedTopics) context.AddFailure("Limits.BlockedTopics", Messages.TooMany(BlockedTopics));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < topics.Count; index++)
            {
                var path = $"Limits.BlockedTopics[{index}]";
                if (Text(context, path, topics[index], BlockedTopicLength, required: true) && !seen.Add(topics[index].Trim()))
                    context.AddFailure(path, Messages.Repeated);
            }
        }

        Text(context, "Limits.CannotPromise", limits.CannotPromise, CannotPromiseLength, required: false);
        if (limits.MaxReplyLength is < MinReplyLength or > MaxReplyLength)
            context.AddFailure("Limits.MaxReplyLength", Messages.OutOfRange(MinReplyLength, MaxReplyLength));
        if (limits.DailyReplyLimit is < MinDailyReplies or > MaxDailyReplies)
            context.AddFailure("Limits.DailyReplyLimit", Messages.OutOfRange(MinDailyReplies, MaxDailyReplies));
        if (limits.AllowedActions is not { Count: > 0 } actions) context.AddFailure("Limits.AllowedActions", Messages.ActionsRequired);
        else if (!actions.All(Enum.IsDefined) || actions.Distinct().Count() != actions.Count) context.AddFailure("Limits.AllowedActions", Messages.Invalid);
    }

    private static void Action(ValidationContext<UpdateWhatsAppAssistantAgentRequest> context, string path, WhatsAppAssistantAction? action,
        IReadOnlyList<WhatsAppAssistantActionKind> allowed, string culture)
    {
        if (action is null || !Enum.IsDefined(action.Kind))
        {
            context.AddFailure(path + ".Kind", Messages.Invalid);
            return;
        }

        if (!allowed.Contains(action.Kind)) context.AddFailure(path + ".Kind", Messages.ActionNotAllowed);
        var buttons = action.Buttons ?? [];
        // Only what the action uses: anything else is a mistake of the client, never something kept for later.
        if (action.Kind != WhatsAppAssistantActionKind.SendForm && action.FormDefinitionId is not null) context.AddFailure(path + ".FormDefinitionId", Messages.Invalid);
        if (action.Kind != WhatsAppAssistantActionKind.ExactText && action.Text is not null) context.AddFailure(path + ".Text", Messages.Invalid);
        if (action.Kind != WhatsAppAssistantActionKind.DraftedReply && !string.IsNullOrWhiteSpace(action.Instructions)) context.AddFailure(path + ".Instructions", Messages.Invalid);
        if (action.Kind != WhatsAppAssistantActionKind.AskButtons && (action.Question is not null || buttons.Count > 0)) context.AddFailure(path + ".Buttons", Messages.Invalid);
        if (action.Kind != WhatsAppAssistantActionKind.Handoff && !string.IsNullOrWhiteSpace(action.HandoffReason)) context.AddFailure(path + ".HandoffReason", Messages.Invalid);
        switch (action.Kind)
        {
            case WhatsAppAssistantActionKind.SendForm when action.FormDefinitionId is not { } form || form == Guid.Empty:
                context.AddFailure(path + ".FormDefinitionId", Messages.FormRequired);
                break;
            case WhatsAppAssistantActionKind.ExactText:
                Fixed(context, path + ".Text", action.Text, ExactTextLength, culture);
                break;
            case WhatsAppAssistantActionKind.DraftedReply:
                Text(context, path + ".Instructions", action.Instructions, InstructionsLength, required: false);
                break;
            case WhatsAppAssistantActionKind.AskButtons:
                Fixed(context, path + ".Question", action.Question, QuestionLength, culture);
                Buttons(context, path + ".Buttons", buttons, allowed, culture);
                break;
            case WhatsAppAssistantActionKind.Handoff:
                Text(context, path + ".HandoffReason", action.HandoffReason, HandoffReasonLength, required: true);
                break;
        }
    }

    private static void Buttons(ValidationContext<UpdateWhatsAppAssistantAgentRequest> context, string path, IReadOnlyList<WhatsAppAssistantButton> buttons,
        IReadOnlyList<WhatsAppAssistantActionKind> allowed, string culture)
    {
        if (buttons.Count == 0) context.AddFailure(path, Messages.ButtonsRequired);
        if (buttons.Count > WhatsAppAssistantAgentLimits.Buttons) context.AddFailure(path, Messages.TooMany(WhatsAppAssistantAgentLimits.Buttons));
        var titles = new Dictionary<string, HashSet<string>> { ["es"] = new(StringComparer.OrdinalIgnoreCase), ["en"] = new(StringComparer.OrdinalIgnoreCase) };
        for (var index = 0; index < buttons.Count; index++)
        {
            var itemPath = $"{path}[{index}]";
            if (buttons[index] is not { } button || !Enum.IsDefined(button.Destination))
            {
                context.AddFailure(itemPath, Messages.Invalid);
                continue;
            }

            if (Fixed(context, itemPath + ".Title", button.Title, ButtonTitleLength, culture)
                && Languages.Any(language => button.Title.In(language) is { } title && !string.IsNullOrWhiteSpace(title) && !titles[language].Add(title.Trim())))
                context.AddFailure(itemPath + ".Title", Messages.Repeated);
            var kind = button.Destination switch
            {
                WhatsAppAssistantButtonDestination.SendForm => WhatsAppAssistantActionKind.SendForm,
                WhatsAppAssistantButtonDestination.ExactText => WhatsAppAssistantActionKind.ExactText,
                _ => WhatsAppAssistantActionKind.Handoff,
            };
            if (!allowed.Contains(kind)) context.AddFailure(itemPath + ".Destination", Messages.ActionNotAllowed);
            if (button.Destination == WhatsAppAssistantButtonDestination.SendForm && (button.FormDefinitionId is not { } form || form == Guid.Empty))
                context.AddFailure(itemPath + ".FormDefinitionId", Messages.FormRequired);
            if (button.Destination != WhatsAppAssistantButtonDestination.SendForm && button.FormDefinitionId is not null)
                context.AddFailure(itemPath + ".FormDefinitionId", Messages.Invalid);
            if (button.Destination == WhatsAppAssistantButtonDestination.ExactText) Fixed(context, itemPath + ".Text", button.Text, ExactTextLength, culture);
            else if (button.Text is not null) context.AddFailure(itemPath + ".Text", Messages.Invalid);
        }
    }

    /// <summary>A fixed text needs the original language; both languages have the same limit.</summary>
    private static bool Fixed(ValidationContext<UpdateWhatsAppAssistantAgentRequest> context, string path, WhatsAppAssistantText? text, int maximumLength, string culture)
    {
        if (text is null || string.IsNullOrWhiteSpace(text.In(culture)))
        {
            context.AddFailure(path, ValidationMessages.Required);
            return false;
        }

        return Text(context, path, text.Es, maximumLength, required: false) & Text(context, path, text.En, maximumLength, required: false);
    }

    private static bool Text(ValidationContext<UpdateWhatsAppAssistantAgentRequest> context, string path, string? value, int maximumLength, bool required)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0)
        {
            if (required) context.AddFailure(path, ValidationMessages.Required);
            return !required;
        }

        if (text.Length > maximumLength)
        {
            context.AddFailure(path, ValidationMessages.MaxLength);
            return false;
        }

        if (!StorableText.IsStorable(text))
        {
            context.AddFailure(path, Messages.Invalid);
            return false;
        }

        return true;
    }
}
