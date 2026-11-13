using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// DOC6 (task 14): the channel forms-documents for «Armar formulario con la IA»: its context and the definitions it offers with a document,
// the documents a request takes (one unclassified, or the group of a topic) and the readings that go to the assistant as its document.
public sealed class FormDocumentAuthoringPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_context_is_the_reviewer_without_a_subject_in_the_request_s_own_conversation_with_the_authoring_capabilities_and_one_terminal()
    {
        var actor = Guid.NewGuid();
        var request = Guid.NewGuid();

        var context = FormDocumentAuthoringPolicy.Context(actor, "es", "America/Argentina/Buenos_Aires", request);

        Assert.Equal(("forms-documents", actor, (Guid?)null, (Guid?)request, (Guid?)null),
            (context.ChannelId, context.ActorUserId, context.SubjectUserId, context.ChannelConversationId, context.FocusDefinitionId));
        Assert.Equal([BusinessFormsPermissions.DocumentsRead, BusinessFormsPermissions.DocumentsReview, BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage],
            context.RequiredPermissions.Actor);
        Assert.Empty(context.RequiredPermissions.Subject);
        Assert.Equal([("forms.authoring.forms", 1), ("forms.authoring.catalogs", 1), ("forms.library.standards", 1), ("forms.authoring.propose-form", 2)],
            context.AllowedCapabilities.Select(capability => (capability.Id, capability.Version)));
        Assert.Equal([new AssistantTerminalReference("assistant_proposal", 1)], context.AllowedTerminals);
        Assert.Equal(AssistantInteractionMode.Unspecified, context.InteractionMode);
        Assert.Null(context.PreloadedCapabilities);
    }

    [Fact]
    public void With_a_document_it_offers_its_four_capabilities_and_its_terminal_five_definitions_of_sixteen()
    {
        var users = new InMemoryUserAccounts();
        var permissions = new FakePermissionService();
        AssistantCapabilityDefinition[] registered =
        [
            .. new BusinessFormAuthoringAssistantCapabilitySource(new AuthoringAssistantFixture.AuthoringReader(), users, permissions).Definitions,
            .. new BusinessFormLibraryAssistantCapabilitySource([new BusinessFormStandardSource()], users, permissions).Definitions,
            .. new BusinessFormDocumentAssistantCapabilitySource(new FormReceivedDocumentFixture(), users, permissions).Definitions,
        ];
        var context = FormDocumentAuthoringPolicy.Context(Guid.NewGuid(), "es", "UTC", Guid.NewGuid());

        var offered = context.AllowedCapabilities.Select(reference => registered.Single(definition => definition.Id == reference.Id
            && definition.Version == reference.Version)).ToArray();

        Assert.All(offered, definition => Assert.True(definition.OfferedWith(AssistantAttachmentKind.Document)));
        Assert.Equal(5, offered.Length + context.AllowedTerminals.Count);
        Assert.Single(offered, definition => definition.Kind == AssistantCapabilityKind.Proposal);
        // Each capability asks the reviewer for its own permission: reading forms or catalogs, or designing forms to propose one.
        Assert.Equal(new[] { BusinessFormsPermissions.Read, BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.Manage }.Order(StringComparer.Ordinal),
            offered.SelectMany(definition => definition.RequiredPermissions.Actor).Distinct().Order(StringComparer.Ordinal));
        // A change of form keeps its three definitions, now in a conversation of its own document.
        var document = Guid.NewGuid();
        var extraction = FormReceivedDocumentExtractionPolicy.Context(Guid.NewGuid(), "es", "UTC", Guid.NewGuid(), document);
        Assert.Equal(3, extraction.AllowedCapabilities.Count + extraction.AllowedTerminals.Count);
        Assert.Equal(document, extraction.ChannelConversationId);
    }

    [Fact]
    public async Task One_document_is_taken_only_while_unclassified_with_a_reading_and_its_copy()
    {
        var f = new FormDocumentProposalFixture();
        var policy = f.Policy();
        var readable = f.Document();
        var withoutReading = f.Document(reading: false);
        var discarded = f.Document();
        Assert.True(discarded.Discard(discarded.Revision, f.Inbox.Clock.GetUtcNow().UtcDateTime).IsSuccess);

        Assert.Equal([readable.Id], (await policy.DocumentAsync(readable.Id, Ct)).Value.Select(row => row.Id));
        Assert.Equal(FormReceivedDocumentErrors.ReadingUnavailable, (await policy.DocumentAsync(withoutReading.Id, Ct)).Error);
        Assert.Equal(FormReceivedDocumentErrors.StateConflict, (await policy.DocumentAsync(discarded.Id, Ct)).Error);
        Assert.Equal(FormReceivedDocumentErrors.NotFound, (await policy.DocumentAsync(Guid.NewGuid(), Ct)).Error);
    }

    [Fact]
    public async Task A_group_takes_its_topic_s_documents_newest_first_that_are_unclassified_and_can_be_read()
    {
        var f = new FormDocumentProposalFixture();
        var now = f.Inbox.Clock.GetUtcNow().UtcDateTime;
        var newest = f.Document(receivedAtUtc: now);
        var unread = f.Document(reading: false, receivedAtUtc: now.AddMinutes(-1));
        var discarded = f.Document(receivedAtUtc: now.AddMinutes(-2));
        Assert.True(discarded.Discard(discarded.Revision, now).IsSuccess);
        var oldest = f.Document(receivedAtUtc: now.AddMinutes(-3));
        var topic = Guid.NewGuid();
        f.Topics[topic] = [newest.Id, unread.Id, discarded.Id, oldest.Id];

        var group = await f.Policy().GroupAsync(topic, Ct);

        // In the topic's order, whatever order the documents come in; one without a reading, or no longer unclassified, is left out.
        Assert.Equal([newest.Id, oldest.Id], group.Value.Select(row => row.Id));
        Assert.Equal(FormReceivedDocumentErrors.GroupUnavailable, (await f.Policy().GroupAsync(Guid.NewGuid(), Ct)).Error);
        f.Topics[topic] = [unread.Id];
        Assert.Equal(FormReceivedDocumentErrors.GroupUnavailable, (await f.Policy().GroupAsync(topic, Ct)).Error);
    }

    [Fact]
    public async Task A_group_takes_at_most_a_request_s_documents()
    {
        var f = new FormDocumentProposalFixture();
        var topic = Guid.NewGuid();
        f.Topics[topic] = Enumerable.Range(0, FormDocumentProposal.MaxDocuments + 3).Select(_ => f.Document().Id).ToArray();

        var group = await f.Policy().GroupAsync(topic, Ct);

        Assert.Equal(f.Topics[topic].Take(FormDocumentProposal.MaxDocuments), group.Value.Select(row => row.Id));
    }

    [Fact]
    public async Task Up_to_five_readings_go_the_first_with_its_copy_and_half_the_budget_and_the_others_cut_to_their_share()
    {
        var f = new FormDocumentProposalFixture();
        var now = f.Inbox.Clock.GetUtcNow().UtcDateTime;
        var documents = Enumerable.Range(0, 6).Select(index => f.Document(index == 1 ? new string('9', 5000) : $"| Total | $ {index} |", fileName: $"ticket-{index}.pdf"))
            .ToArray();
        var rows = new List<FormReceivedDocumentStoredRow>();
        foreach (var document in documents) rows.Add((await f.Inbox.FindAsync(document.Id, Ct))!);
        var context = FormDocumentAuthoringPolicy.Context(f.Reviewer, "es", "UTC", Guid.NewGuid());

        var operation = f.Policy().Request(Guid.NewGuid(), context, rows, now);

        Assert.NotNull(operation);
        var attachment = operation.Attachment!;
        Assert.Equal((AssistantAttachmentKind.Document, documents[0].StorageKey, "ticket-0.pdf", documents[0].Sha256),
            (attachment.Kind, attachment.StorageKey, attachment.Name, attachment.Sha256));
        Assert.Equal("| Total | $ 0 |", attachment.Document!.Reading!.Text);
        Assert.Equal(["ticket-1.pdf", "ticket-2.pdf", "ticket-3.pdf", "ticket-4.pdf"], attachment.MoreDocuments!.Select(document => document.FileName));
        // The long one is cut by the server to its share of the other half, and says so.
        var cut = attachment.MoreDocuments![0];
        Assert.True(cut.Reading!.Truncated);
        Assert.True(AssistantAttachmentInput.SerializedBytes(cut) <= AssistantAttachmentInput.DocumentBytes(1, 5));
        Assert.Equal((now, now.AddDays(FormDocumentAuthoringPolicy.ContentDays)), (operation.OccurredAtUtc, operation.ContentExpiresAtUtc));
        Assert.Same(context, operation.Context);
        Assert.Contains("formulario", operation.Text, StringComparison.Ordinal);
        Assert.Contains("form", f.Policy().Request(Guid.NewGuid(), context with { Culture = "en" }, rows, now)!.Text, StringComparison.Ordinal);
        Assert.Null(f.Policy().Request(Guid.NewGuid(), context, [], now));
    }

    [Fact]
    public void The_proposal_read_back_is_only_propose_form_2()
    {
        var turn = Guid.NewGuid();
        var form = new FormAuthoringDocumentFormProposal("es", "Tickets", "TCB", "Cargar", [new("Total", "number", true, [], null, "52.360,00")],
            new("Un ticket de combustible."), null);
        var proposal = new AssistantObservedProposal(turn, Guid.NewGuid(), FormDocumentAuthoringPolicy.ProposeForm, form);
        var other = new AssistantObservedProposal(turn, Guid.NewGuid(), new(BusinessFormAuthoringAssistantCapabilitySource.ProposeFormId, 1),
            form.ToForm());

        Assert.Same(proposal, FormDocumentAuthoringPolicy.Proposal(new(Guid.NewGuid(), "completed", proposal, null, null) { Proposals = [proposal] }));
        Assert.Same(proposal, FormDocumentAuthoringPolicy.Proposal(new(Guid.NewGuid(), "completed", proposal, null, null)));
        Assert.Null(FormDocumentAuthoringPolicy.Proposal(new(Guid.NewGuid(), "completed", other, null, null) { Proposals = [other] }));
    }
}
