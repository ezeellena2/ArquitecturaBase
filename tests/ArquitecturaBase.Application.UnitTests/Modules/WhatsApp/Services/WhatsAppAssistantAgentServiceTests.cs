using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>Casos del asistente (AS1-A): read the main agent and save the four tabs with the revision read, 409 if another save came first.</summary>
public sealed class WhatsAppAssistantAgentServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Form = Guid.Parse("018f0000-0000-7000-8000-000000000001");

    [Fact]
    public async Task Before_the_first_save_it_reads_the_defaults_in_the_language_of_the_menu()
    {
        var host = new Host();
        host.Forms.AssistantForms.Add(new(Form, "Registrar visita", "Cuando avisa que terminó una visita."));
        using var _ = new UiCulture("en-US");

        var agent = await host.Service.GetAsync(Ct);

        Assert.True(agent.IsSuccess);
        Assert.Equal(0, agent.Value.Revision);
        Assert.Equal("en", agent.Value.OriginalCulture);
        Assert.Equal(WhatsAppAssistantActionKind.NoReply, agent.Value.Fallback.Kind);
        Assert.Empty(agent.Value.Cases);
        Assert.Equal(400, agent.Value.Limits.MaxReplyLength);
        var form = Assert.Single(agent.Value.Forms);
        Assert.Equal((Form, "Registrar visita", "Cuando avisa que terminó una visita."), (form.DefinitionId, form.Name, form.OfferWhen));
        Assert.Equal("en", host.Forms.AssistantFormsCulture);
        Assert.Equal("en", host.Knowledge.Culture);
        Assert.Equal("available", agent.Value.AssistantAvailability.State);
        Assert.Equal(0, host.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task The_first_save_creates_the_main_agent_in_its_original_language()
    {
        var host = new Host();

        var saved = await host.Service.UpdateAsync(Request(0, "es"), Ct);

        Assert.True(saved.IsSuccess);
        Assert.Equal(1, saved.Value.Revision);
        Assert.Equal("es", saved.Value.OriginalCulture);
        Assert.Equal("Asistente", saved.Value.Profile.Name);
        var agent = Assert.Single(host.Agents.Added);
        Assert.True(agent.IsMain);
        Assert.Equal(1, host.Agents.Locks);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Another_save_first_is_a_conflict_and_writes_nothing(long expectedRevision)
    {
        var host = new Host();
        await host.Service.UpdateAsync(Request(0, "es"), Ct);
        var revision = host.Agents.Main!.Revision;

        var conflict = await host.Service.UpdateAsync(Request(expectedRevision, "es") with { Profile = Profile() with { Name = "Otro" } }, Ct);

        Assert.Equal(WhatsAppAssistantAgentErrors.RevisionConflict, conflict.Error);
        Assert.Equal(revision, host.Agents.Main.Revision);
        Assert.Equal("Asistente", host.Agents.Main.Profile.Name);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Without_an_agent_only_the_revision_zero_saves_and_the_language_cannot_change()
    {
        var host = new Host();

        Assert.Equal(WhatsAppAssistantAgentErrors.RevisionConflict, (await host.Service.UpdateAsync(Request(1, "es"), Ct)).Error);
        await host.Service.UpdateAsync(Request(0, "es"), Ct);
        Assert.Equal(WhatsAppAssistantAgentErrors.RevisionConflict, (await host.Service.UpdateAsync(Request(1, "en"), Ct)).Error);
        Assert.Equal("es", host.Agents.Main!.OriginalCulture);
    }

    [Fact]
    public async Task Saving_the_same_configuration_keeps_the_revision()
    {
        var host = new Host();
        var request = Request(0, "es");
        await host.Service.UpdateAsync(request, Ct);

        var again = await host.Service.UpdateAsync(request with { ExpectedRevision = 1 }, Ct);

        Assert.True(again.IsSuccess);
        Assert.Equal(1, again.Value.Revision);
    }

    [Fact]
    public async Task A_wrong_field_answers_at_its_path_before_opening_the_transaction()
    {
        var host = new Host();
        var onlyTexts = Limits() with { AllowedActions = [WhatsAppAssistantActionKind.ExactText, WhatsAppAssistantActionKind.NoReply] };
        var request = Request(0, "es") with
        {
            Limits = onlyTexts,
            Cases = [new(Guid.CreateVersion7(), " ", WhatsAppAssistantInputType.Any, true, new(WhatsAppAssistantActionKind.DraftedReply))],
        };

        var invalid = await host.Service.UpdateAsync(request, Ct);

        var validation = Assert.IsType<ValidationError>(invalid.Error);
        Assert.Contains("cases[0].when", validation.Errors.Keys);
        Assert.Contains("cases[0].action.kind", validation.Errors.Keys);
        Assert.Equal(0, host.UnitOfWork.Transactions);
    }

    // A text over its length names its limit with the thousands of its language (2026-11-12): AddFailure does not fill the
    // placeholder of the message by itself, so the validator gives it the limit.
    [Fact]
    public async Task A_text_over_its_length_names_its_limit_with_its_thousands()
    {
        using var culture = new CultureScope("es");
        var host = new Host();
        var drafted = new WhatsAppAssistantAction(WhatsAppAssistantActionKind.DraftedReply, Instructions: new string('a', WhatsAppAssistantAgentLimits.InstructionsLength + 1));
        var request = Request(0, "es") with { Cases = [new(Guid.CreateVersion7(), "Pregunta por un precio", WhatsAppAssistantInputType.Text, true, drafted)] };

        var validation = Assert.IsType<ValidationError>((await host.Service.UpdateAsync(request, Ct)).Error);

        Assert.Contains("Ingresá como máximo 1.000 caracteres.", validation.Errors["cases[0].action.instructions"]);
    }

    [Fact]
    public async Task Buttons_and_fixed_texts_answer_at_their_paths()
    {
        var host = new Host();
        var ask = new WhatsAppAssistantAction(WhatsAppAssistantActionKind.AskButtons, Question: new(null, "Visit or delivery?"),
            Buttons: [new(new("Visita", null), WhatsAppAssistantButtonDestination.SendForm, null, null), new(new("visita", null), WhatsAppAssistantButtonDestination.Handoff, null, null)]);
        var request = Request(0, "es") with { Cases = [new(Guid.CreateVersion7(), "Pasó por un cliente", WhatsAppAssistantInputType.Text, true, ask)] };

        var invalid = await host.Service.UpdateAsync(request, Ct);

        var errors = Assert.IsType<ValidationError>(invalid.Error).Errors;
        Assert.Contains("cases[0].action.question", errors.Keys);
        Assert.Contains("cases[0].action.buttons[0].formDefinitionId", errors.Keys);
        Assert.Contains("cases[0].action.buttons[1].title", errors.Keys);
    }

    // As the entity checks it: a title without its translation (or with only spaces) goes out in its original, so each language
    // compares what the person sees, without telling capitals apart, and every title counts even after a repeated one. Each button
    // is «es|en».
    [Theory]
    [InlineData("es", new[] { "OK|", "Listo|OK" }, new[] { 1 })]
    [InlineData("es", new[] { "Listo|OK", "OK|" }, new[] { 1 })]
    [InlineData("es", new[] { "OK|  ", "Listo|ok" }, new[] { 1 })]
    [InlineData("en", new[] { "|OK", "OK|Done" }, new[] { 1 })]
    [InlineData("es", new[] { "Visita|Visit", "visita|Delivery", "Entrega|delivery" }, new[] { 1, 2 })]
    public async Task A_title_repeated_as_the_person_sees_it_answers_at_that_title(string original, string[] buttons, int[] repeated)
    {
        static string? Written(string text) => text is "" ? null : text;
        var host = new Host();
        var ask = new WhatsAppAssistantAction(WhatsAppAssistantActionKind.AskButtons, Question: new("¿Ya terminaste?", "Are you done?"),
            Buttons: [.. buttons.Select(button => button.Split('|')).Select(texts => new WhatsAppAssistantButton(new(Written(texts[0]), Written(texts[1])),
                WhatsAppAssistantButtonDestination.Handoff, null, null))]);
        var request = Request(0, original) with { Cases = [new(Guid.CreateVersion7(), "Terminó una tarea", WhatsAppAssistantInputType.Text, true, ask)] };

        var errors = Assert.IsType<ValidationError>((await host.Service.UpdateAsync(request, Ct)).Error).Errors;

        Assert.Equal(repeated.Select(index => $"cases[0].action.buttons[{index}].title"), errors.Keys.Order(StringComparer.Ordinal));
    }

    private static UpdateWhatsAppAssistantAgentRequest Request(long revision, string culture) => new(revision, culture, Profile(),
        [new(Guid.Parse("018f0000-0000-7000-8000-0000000000c1"), "Pide hablar con una persona.", WhatsAppAssistantInputType.Any, true,
            new(WhatsAppAssistantActionKind.Handoff, HandoffReason: "Pide hablar con una persona."))],
        new(WhatsAppAssistantActionKind.DraftedReply, Instructions: "Respondé breve."), Limits());

    private static WhatsAppAssistantProfile Profile() => new("Asistente", "Soy el asistente.", "Cercano.", "Responde consultas.", "No da precios.",
        new("Te paso con una persona del equipo.", "I'll hand you over to someone on the team."));

    private static WhatsAppAssistantLimits Limits() => WhatsAppAssistantAgentLimits.DefaultLimits;

    private sealed class Host
    {
        public Agents Agents { get; } = new();
        public FakeAssistantForms Forms { get; } = new();
        public Knowledge Knowledge { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public WhatsAppAssistantAgentService Service { get; }

        public Host()
        {
            var operations = new WhatsAppAssistantSuggestionFixture.Operations(UnitOfWork);
            Service = new(Agents, Agents, RequestValidators.For(new UpdateWhatsAppAssistantAgentRequestValidator()), UnitOfWork, Forms, Knowledge,
                operations, NullLogger<WhatsAppAssistantAgentService>.Instance);
        }
    }

    private sealed class Agents : IWhatsAppAssistantAgentRepository, IWhatsAppAssistantAgentReader
    {
        public WhatsAppAssistantAgent? Main { get; private set; }
        public List<WhatsAppAssistantAgent> Added { get; } = [];
        public int Locks { get; private set; }
        public Task LockAsync(CancellationToken cancellationToken) { Locks++; return Task.CompletedTask; }
        public Task<WhatsAppAssistantAgent?> GetMainAsync(CancellationToken cancellationToken) => Task.FromResult(Main);
        public void Add(WhatsAppAssistantAgent agent) { Added.Add(agent); Main = agent; }
        public Task<WhatsAppAssistantAgentRow?> FindMainAsync(CancellationToken cancellationToken) => Task.FromResult(Main is { } agent
            ? new WhatsAppAssistantAgentRow(agent.Revision, agent.OriginalCulture, agent.Profile, agent.Cases, agent.Fallback, agent.Limits) : null);
        public Task<IReadOnlyList<WhatsAppAssistantTestRecipientRow>> ListTestRecipientsAsync(string? search, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WhatsAppAssistantTestRecipientRow>>([]);
    }

    private sealed class Knowledge : IBusinessKnowledgeSource
    {
        public string? Culture { get; private set; }
        public Task<BusinessKnowledge> ReadAsync(string culture, CancellationToken cancellationToken)
        {
            Culture = culture;
            return Task.FromResult(BusinessKnowledge.NotInstalled);
        }
    }

    private sealed class UiCulture : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentUICulture;
        public UiCulture(string name) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}
