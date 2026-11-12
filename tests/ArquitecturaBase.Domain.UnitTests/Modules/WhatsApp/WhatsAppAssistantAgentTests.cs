using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

/// <summary>Casos del asistente (AS1-A, AS2, AS5): the main agent with its profile, cases, «Cualquier otra cosa» and limits.</summary>
public sealed class WhatsAppAssistantAgentTests
{
    private static readonly Guid Form = Guid.Parse("018f0000-0000-7000-8000-000000000001");

    [Fact]
    public void A_new_agent_is_the_main_one_in_its_original_language_and_unsaved()
    {
        var agent = WhatsAppAssistantAgent.Create("en");

        Assert.True(agent.IsMain);
        Assert.Equal("en", agent.OriginalCulture);
        Assert.Equal(0, agent.Revision);
        Assert.Throws<ArgumentException>(() => WhatsAppAssistantAgent.Create("pt"));
    }

    [Fact]
    public void Saving_normalizes_the_texts_and_advances_the_revision()
    {
        var agent = WhatsAppAssistantAgent.Create("es");

        var saved = agent.Update(Profile() with { Name = "  Asistente de Arquitectura Base  " },
            [Case(" Pide hablar con una persona. ", new(WhatsAppAssistantActionKind.Handoff, HandoffReason: " Pide hablar con una persona. "))],
            new(WhatsAppAssistantActionKind.DraftedReply, Instructions: " Respondé breve. "), Limits() with { BlockedTopics = [" Precios ", "Reclamos"] });

        Assert.True(saved.IsSuccess);
        Assert.True(saved.Value);
        Assert.Equal(1, agent.Revision);
        Assert.Equal("Asistente de Arquitectura Base", agent.Profile.Name);
        Assert.Equal("Pide hablar con una persona.", agent.Cases[0].When);
        Assert.Equal("Pide hablar con una persona.", agent.Cases[0].Action.HandoffReason);
        Assert.Equal("Respondé breve.", agent.Fallback.Instructions);
        Assert.Equal(["Precios", "Reclamos"], agent.Limits.BlockedTopics);
    }

    [Fact]
    public void Saving_without_changes_does_not_advance_the_revision()
    {
        var agent = WhatsAppAssistantAgent.Create("es");
        var hours = Case("Pregunta el horario.", ExactText());
        Assert.True(agent.Update(Profile(), [hours], NoReply(), Limits()).IsSuccess);

        var again = agent.Update(Profile() with { Name = " Asistente " }, [hours with { When = "Pregunta el horario. " }], NoReply(), Limits());

        Assert.True(again.IsSuccess);
        Assert.False(again.Value);
        Assert.Equal(1, agent.Revision);
    }

    [Fact]
    public void The_profile_needs_a_name_and_the_handoff_message_in_its_original_language()
    {
        var agent = WhatsAppAssistantAgent.Create("es");

        Assert.Equal(WhatsAppAssistantAgentErrors.Invalid, agent.Update(Profile() with { Name = " " }, [], NoReply(), Limits()).Error);
        Assert.Equal(WhatsAppAssistantAgentErrors.Invalid, agent.Update(Profile() with { HandoffMessage = new(null, "I'll hand you over.") }, [], NoReply(), Limits()).Error);
        Assert.True(WhatsAppAssistantAgent.Create("en").Update(Profile() with { HandoffMessage = new(null, "I'll hand you over.") }, [], NoReply(), Limits()).IsSuccess);
        Assert.Equal(0, agent.Revision);
    }

    [Fact]
    public void A_case_needs_what_its_action_asks_for()
    {
        Assert.False(Saves(Case("x", new(WhatsAppAssistantActionKind.SendForm))));
        Assert.True(Saves(Case("x", new(WhatsAppAssistantActionKind.SendForm, FormDefinitionId: Form))));
        Assert.False(Saves(Case("x", new(WhatsAppAssistantActionKind.ExactText, Text: new(" ", null)))));
        Assert.False(Saves(Case("x", new(WhatsAppAssistantActionKind.Handoff))));
        Assert.True(Saves(Case("x", new(WhatsAppAssistantActionKind.DraftedReply))));
        Assert.True(Saves(Case("x", new(WhatsAppAssistantActionKind.NoReply))));
        // Only what the action uses: a form on an exact text is an error, not something kept for later.
        Assert.False(Saves(Case("x", ExactText() with { FormDefinitionId = Form })));
        Assert.False(Saves(Case(" ", NoReply())));
        Assert.False(Saves(Case(new string('a', WhatsAppAssistantAgentLimits.WhenLength + 1), NoReply())));
    }

    [Fact]
    public void Buttons_follow_whatsapp_and_each_one_has_its_destination()
    {
        WhatsAppAssistantButton Button(string title) => new(new(title, null), WhatsAppAssistantButtonDestination.Handoff, null, null);
        WhatsAppAssistantAction Ask(params WhatsAppAssistantButton[] buttons) =>
            new(WhatsAppAssistantActionKind.AskButtons, Question: new("¿Fue una visita o una entrega?", null), Buttons: buttons);

        Assert.True(Saves(Case("x", Ask(new(new("Visita", null), WhatsAppAssistantButtonDestination.SendForm, Form, null),
            new(new("Horario", null), WhatsAppAssistantButtonDestination.ExactText, null, new("De 8 a 17.", null)), Button("Persona")))));
        Assert.False(Saves(Case("x", Ask())));
        Assert.False(Saves(Case("x", Ask(Button("a"), Button("b"), Button("c"), Button("d")))));
        Assert.False(Saves(Case("x", Ask(Button("Visita"), Button("visita ")))));
        Assert.False(Saves(Case("x", Ask(Button(new string('a', WhatsAppAssistantAgentLimits.ButtonTitleLength + 1))))));
        Assert.False(Saves(Case("x", Ask(new WhatsAppAssistantButton(new("Visita", null), WhatsAppAssistantButtonDestination.SendForm, null, null)))));
        Assert.False(Saves(Case("x", Ask(Button("Visita")) with { Question = null })));
    }

    // A button without its translation goes out with its original title (WhatsAppAssistantText.For) and WhatsApp refuses two equal
    // titles, so they are compared as the person sees them in each language, not only among the translations written.
    [Theory]
    [InlineData("es", "OK", null, "Listo", "OK", false)]
    [InlineData("es", "Listo", "OK", "OK", null, false)]
    [InlineData("en", null, "OK", "OK", "Done", false)]
    [InlineData("es", "OK", "  ", "Listo", "ok", false)]
    [InlineData("es", "OK", "Fine", "Listo", "OK", true)]
    public void Two_buttons_never_show_the_same_title_in_a_language_even_through_their_original(string original, string? firstEs, string? firstEn,
        string? secondEs, string? secondEn, bool saves)
    {
        WhatsAppAssistantButton Button(string? es, string? en) => new(new(es, en), WhatsAppAssistantButtonDestination.Handoff, null, null);
        var ask = new WhatsAppAssistantAction(WhatsAppAssistantActionKind.AskButtons, Question: new("¿Ya terminaste?", "Are you done?"),
            Buttons: [Button(firstEs, firstEn), Button(secondEs, secondEn)]);
        var profile = Profile() with { HandoffMessage = new("Te paso con una persona del equipo.", "I'll hand you over to someone on the team.") };

        Assert.Equal(saves, WhatsAppAssistantAgent.Create(original).Update(profile, [Case("x", ask)], NoReply(), Limits()).IsSuccess);
    }

    [Fact]
    public void Every_action_and_destination_has_to_be_allowed_for_the_agent()
    {
        var onlyTexts = Limits() with { AllowedActions = [WhatsAppAssistantActionKind.ExactText, WhatsAppAssistantActionKind.NoReply] };
        var handoffButton = new WhatsAppAssistantAction(WhatsAppAssistantActionKind.AskButtons, Question: new("¿Seguimos?", null),
            Buttons: [new(new("Persona", null), WhatsAppAssistantButtonDestination.Handoff, null, null)]);

        Assert.True(WhatsAppAssistantAgent.Create("es").Update(Profile(), [Case("x", ExactText())], NoReply(), onlyTexts).IsSuccess);
        Assert.False(WhatsAppAssistantAgent.Create("es").Update(Profile(), [Case("x", new(WhatsAppAssistantActionKind.DraftedReply))], NoReply(), onlyTexts).IsSuccess);
        Assert.False(WhatsAppAssistantAgent.Create("es").Update(Profile(), [], new(WhatsAppAssistantActionKind.DraftedReply), onlyTexts).IsSuccess);
        Assert.False(WhatsAppAssistantAgent.Create("es").Update(Profile(), [Case("x", handoffButton)], NoReply(),
            onlyTexts with { AllowedActions = [WhatsAppAssistantActionKind.AskButtons, WhatsAppAssistantActionKind.NoReply] }).IsSuccess);
        Assert.False(WhatsAppAssistantAgent.Create("es").Update(Profile(), [], NoReply(), Limits() with { AllowedActions = [] }).IsSuccess);
    }

    [Fact]
    public void The_cases_are_bounded_and_each_keeps_its_own_identity()
    {
        var cases = Enumerable.Range(0, WhatsAppAssistantAgentLimits.Cases + 1).Select(index => Case($"Caso {index}", NoReply())).ToArray();
        var repeated = Case("Uno", NoReply());

        Assert.False(WhatsAppAssistantAgent.Create("es").Update(Profile(), cases, NoReply(), Limits()).IsSuccess);
        Assert.True(WhatsAppAssistantAgent.Create("es").Update(Profile(), cases[..^1], NoReply(), Limits()).IsSuccess);
        Assert.False(WhatsAppAssistantAgent.Create("es").Update(Profile(), [repeated, repeated with { When = "Dos" }], NoReply(), Limits()).IsSuccess);
        Assert.False(WhatsAppAssistantAgent.Create("es").Update(Profile(), [repeated with { Id = Guid.Empty }], NoReply(), Limits()).IsSuccess);
    }

    [Fact]
    public void The_limits_have_their_ranges()
    {
        bool Saves(WhatsAppAssistantLimits limits) => WhatsAppAssistantAgent.Create("es").Update(Profile(), [], NoReply(), limits).IsSuccess;

        Assert.True(Saves(Limits() with { MaxReplyLength = WhatsAppAssistantAgentLimits.MinReplyLength, DailyReplyLimit = WhatsAppAssistantAgentLimits.MaxDailyReplies }));
        Assert.False(Saves(Limits() with { MaxReplyLength = WhatsAppAssistantAgentLimits.MinReplyLength - 1 }));
        Assert.False(Saves(Limits() with { MaxReplyLength = WhatsAppAssistantAgentLimits.MaxReplyLength + 1 }));
        Assert.False(Saves(Limits() with { DailyReplyLimit = 0 }));
        Assert.False(Saves(Limits() with { BlockedTopics = ["Precios", "precios"] }));
        Assert.False(Saves(Limits() with { BlockedTopics = [" "] }));
        Assert.False(Saves(Limits() with { BlockedTopics = [.. Enumerable.Range(0, WhatsAppAssistantAgentLimits.BlockedTopics + 1).Select(index => $"Tema {index}")] }));
    }

    [Fact]
    public void A_fixed_text_goes_out_in_the_language_of_the_conversation_or_in_the_original()
    {
        var text = new WhatsAppAssistantText("Atendemos de 8 a 17.", null);

        Assert.Equal("Atendemos de 8 a 17.", text.For("en", "es"));
        Assert.Equal("We're open from 8 to 5.", (text with { En = "We're open from 8 to 5." }).For("en", "es"));
    }

    [Fact]
    public void The_defaults_answer_nothing_else_until_the_profile_is_saved()
    {
        var defaults = WhatsAppAssistantAgentLimits.DefaultLimits;

        Assert.Equal(WhatsAppAssistantActionKind.NoReply, WhatsAppAssistantAgent.Create("es").Fallback.Kind);
        Assert.Equal(400, defaults.MaxReplyLength);
        Assert.Equal(10, defaults.DailyReplyLimit);
        Assert.Equal(Enum.GetValues<WhatsAppAssistantActionKind>(), defaults.AllowedActions);
    }

    private static bool Saves(WhatsAppAssistantCase @case) => WhatsAppAssistantAgent.Create("es").Update(Profile(), [@case], NoReply(), Limits()).IsSuccess;

    private static WhatsAppAssistantProfile Profile() => new("Asistente", "Soy el asistente.", "Cercano y breve.", "Responde consultas.",
        "No da precios.", new("Te paso con una persona del equipo.", null));

    private static WhatsAppAssistantCase Case(string when, WhatsAppAssistantAction action) =>
        new(Guid.CreateVersion7(), when, WhatsAppAssistantInputType.Any, true, action);

    private static WhatsAppAssistantAction ExactText() => new(WhatsAppAssistantActionKind.ExactText, Text: new("Atendemos de 8 a 17.", null));
    private static WhatsAppAssistantAction NoReply() => new(WhatsAppAssistantActionKind.NoReply);
    private static WhatsAppAssistantLimits Limits() => WhatsAppAssistantAgentLimits.DefaultLimits;
}
