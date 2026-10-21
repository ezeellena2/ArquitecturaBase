using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMissingRequestPrivacyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, bool, bool> MetadataCases
    {
        get
        {
            var result = new TheoryData<string, bool, bool>();
            foreach (var (text, allowed) in new (string, bool)[]
            {
                ("Pedidos a persona＠example.invalid", false),
                ("Pedidos en ｗｗｗ．example.invalid", false),
                ("Pedidos al +54 (11) 5555-1234", false),
                ("Pedidos con ID 1234567", false),
                ("Pedidos del 2026-02-30", false),
                ("Pedidos del 20261019", false),
                ("Pedidos al +54 11 2026-10-19", false),
                ("Monto 1234567", false),
                ("Pedidos del 2026-10-19", true),
                ("Pedidos del 2024-02-29", true),
                ("Pedidos del 2026-10-19, 2026-10-20", true),
                ("Pedidos del 2026-10-19, 2026-02-30", false),
                ("Pagos de ARS 1.234.567,89", true),
                ("Payments of USD 1234567.89", true)
            })
            foreach (var capture in new[] { false, true }) result.Add(text, allowed, capture);
            return result;
        }
    }

    [Theory]
    [MemberData(nameof(MetadataCases))]
    public async Task Generalized_metadata_checks_contact_details_without_confusing_dates_or_explicit_amounts(string text, bool allowed, bool capture)
    {
        var fixture = new FormMissingRequestFixture();
        var request = WithMetadata(fixture.Request(), "Registrar solicitudes", text);

        var result = await ExecuteAsync(fixture, request, capture);

        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed)
        {
            Assert.Equal(FormMissingRequestErrors.ContentInvalid.Code, result.Error.Code);
            Assert.Equal(0, fixture.Uow.Transactions);
        }
        else if (capture)
        {
            Assert.Equal(text, Assert.Single(fixture.Topics).Description);
            Assert.Single(fixture.Requests);
        }
        if (!capture || !allowed)
        {
            Assert.Empty(fixture.Topics);
            Assert.Empty(fixture.Requests);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Known_actor_and_subject_names_are_rejected_without_accents_at_both_boundaries(bool actor, bool capture)
    {
        var fixture = new FormMissingRequestFixture();
        var userId = actor ? fixture.Manager : fixture.Assistant.UserId;
        await fixture.Assistant.Users.ArrangeAsync(users => users.UpdateProfileAsync(userId, "José Pérez", "es", "UTC", Ct));
        var request = WithMetadata(fixture.Request(), "Visitas de Jose Perez", "Piden registrar visitas.");
        if (actor) request = Manual(request, fixture.Manager);

        var result = await ExecuteAsync(fixture, request, capture);

        Assert.True(result.IsFailure);
        Assert.Equal(FormMissingRequestErrors.ContentInvalid.Code, result.Error.Code);
        Assert.Equal(0, fixture.Uow.Transactions);
        Assert.Empty(fixture.Topics);
        Assert.Empty(fixture.Requests);
    }

    [Theory]
    [InlineData("Registrar mañana")]
    [InlineData("Anatomía del pedido")]
    public async Task A_known_name_inside_another_word_does_not_reject_a_generalized_proposal(string name)
    {
        var fixture = new FormMissingRequestFixture();
        await fixture.Assistant.Users.ArrangeAsync(users => users.UpdateProfileAsync(fixture.Assistant.UserId, "Ana", "es", "UTC", Ct));
        var request = WithMetadata(fixture.Request(), name, "Piden registrar tareas.");

        Assert.True((await ExecuteAsync(fixture, request, false)).IsSuccess);
        Assert.Equal(0, fixture.Uow.Transactions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_rechecks_known_identifiers_after_acquiring_the_mutation_lock(bool actor)
    {
        var fixture = new FormMissingRequestFixture();
        var request = WithMetadata(fixture.Request(), "Visitas de Jose Perez", "Piden registrar visitas.");
        if (actor) request = Manual(request, fixture.Manager);
        var userId = actor ? fixture.Manager : fixture.Assistant.UserId;
        fixture.OnLock = () => fixture.Assistant.Users.ArrangeAsync(users => users.UpdateProfileAsync(userId, "José Pérez", "es", "UTC", Ct));

        var result = await fixture.Service.CaptureAsync(request, Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(FormMissingRequestErrors.ContentInvalid.Code, result.Error.Code);
        Assert.Equal(1, fixture.Locks);
        Assert.Equal(1, fixture.Uow.Transactions);
        Assert.Equal(1, fixture.Uow.Rollbacks);
        Assert.Equal(0, fixture.Uow.Commits);
        Assert.Empty(fixture.Topics);
        Assert.Empty(fixture.Requests);
    }

    private static StructuredMissingFormRequestCaptureRequest WithMetadata(StructuredMissingFormRequestCaptureRequest request, string name, string description)
    {
        var payload = Assert.IsType<MissingFormRequestProposalPayload>(request.Proposal.Payload);
        return request with { Proposal = request.Proposal with { Payload = payload with { Name = name, Description = description } } };
    }

    private static StructuredMissingFormRequestCaptureRequest Manual(StructuredMissingFormRequestCaptureRequest request, Guid actor) =>
        request with { Context = request.Context with { ChannelId = "whatsapp-manual", InteractionMode = AssistantInteractionMode.Manual, ActorUserId = actor },
            Provenance = request.Provenance with { ChannelId = "whatsapp-manual" } };

    private static async Task<Result> ExecuteAsync(FormMissingRequestFixture fixture, StructuredMissingFormRequestCaptureRequest request, bool capture)
    {
        if (capture) return await fixture.Service.CaptureAsync(request, Ct);
        var value = Assert.IsType<MissingFormRequestProposalPayload>(request.Proposal.Payload);
        var proposed = await new BusinessFormMissingRequestAssistantCapabilitySource(fixture.Assistant.Candidates, fixture.Assistant.Users).ExecuteAsync(
            new AssistantCapabilityRequest(request.Proposal.TurnId, request.Context, request.Proposal.Capability,
                FormDraftPolicy.Serialize(new { existingTopicId = value.ExistingTopicId, name = value.Name, description = value.Description }),
                request.Proposal.ObservedReads), Ct);
        if (proposed.IsSuccess)
        {
            var payload = Assert.IsType<MissingFormRequestProposalPayload>(proposed.Value.Payload);
            Assert.Equal(value.Name, payload.Name);
            Assert.Equal(value.Description, payload.Description);
        }
        return proposed;
    }
}
