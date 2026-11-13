using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// IA3-A with PostgreSQL: the summary is not an offer, only the latest summary's buttons count, «Crear» replies with the link and
// «Cambiar algo» makes the next message continue the operation. The owner is a neutral double; BusinessForms proves its part.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantAuthoringIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private sealed record AuthoringPayload : AssistantCapabilityPayload;

    [Fact]
    public async Task The_latest_summary_buttons_create_wait_for_a_change_and_the_next_message_continues_its_operation()
    {
        var owner = new WhatsAppAuthoringChannelDouble();
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IStructuredAuthoringProposalChannel>(owner))));
        var contact = await host.ContactAsync(Ct, automatic: true);

        var first = await SummaryAsync(host, contact.ContactId);
        var summary = await ReferenceAsync(host, first.RequestId);
        Assert.Equal(WhatsAppAssistantRequestState.Sent, summary.State);
        var buttons = await OutgoingAsync(host, summary.OutgoingId!.Value);
        Assert.Equal(WhatsAppOutgoingMessageKind.QuickReplies, buttons.Kind);
        Assert.StartsWith("Te propongo este formulario:\n*Parte de lluvias · LLU*", buttons.Body, StringComparison.Ordinal);
        Assert.Equal(["Crear borrador", "Cambiar algo"], buttons.QuickReplies.Select(reply => reply.Title));
        var day = host.Clock.GetUtcNow().UtcDateTime.Date;
        var reads = await host.ScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IWhatsAppAssistantReader>();
            return (Offers: await reader.CountOffersAsync(contact.ContactId, day, day.AddDays(1), Ct), Latest: await reader.FindLatestAuthoringRequestAsync(contact.ContactId, Ct));
        });
        Assert.Equal((0, (Guid?)first.RequestId), reads);

        var create = await TapAsync(host, contact.ContactId, buttons.QuickReplies[0].Id);
        Assert.Contains(contact.ContactId, await PendingAsync(host));
        await ProcessTapAsync(host, contact.ContactId);
        Assert.Equal(summary.ProposalId, Assert.Single(owner.Created).Proposal.Id);
        var created = (await ReplyAsync(host, contact.ContactId, create)).Body!;
        Assert.StartsWith("Listo: armé el borrador Parte de lluvias. Revisalo y publicalo desde la app: http", created, StringComparison.Ordinal);
        Assert.EndsWith($"/formularios/{owner.DefinitionId:D}", created, StringComparison.Ordinal);
        Assert.DoesNotContain(contact.ContactId, await PendingAsync(host));
        Assert.NotNull(await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AsNoTracking()
            .Where(message => message.Id == create).Select(message => message.AssistantProcessedAtUtc).SingleAsync(Ct)));

        var change = await TapAsync(host, contact.ContactId, buttons.QuickReplies[1].Id);
        await ProcessTapAsync(host, contact.ContactId);
        Assert.Equal("Decime qué cambio, por escrito o con un audio.", (await ReplyAsync(host, contact.ContactId, change)).Body);
        Assert.Equal(first.RequestId, await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantReader>().FindChangeRequestAsync(contact.ContactId, Ct)));

        await host.AddNewerSourceAsync(contact, Ct);
        var second = await SummaryAsync(host, contact.ContactId);
        Assert.Equal(first.RequestId, second.Operation!.PreviousRequestId);
        Assert.Equal(first.RequestId, (await ReferenceAsync(host, second.RequestId)).PreviousRequestId);
        Assert.Equal(second.RequestId, await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantReader>().FindLatestAuthoringRequestAsync(contact.ContactId, Ct)));

        var stale = await TapAsync(host, contact.ContactId, buttons.QuickReplies[0].Id);
        await ProcessTapAsync(host, contact.ContactId);
        Assert.Equal("Esa propuesta ya cambió. Usá los botones de la última.", (await ReplyAsync(host, contact.ContactId, stale)).Body);
        Assert.Single(owner.Created);
    }

    // Casos del asistente: a case's question is also the own outgoing of an automatic reference and has buttons, but it is no
    // summary, so the summary before it keeps its buttons.
    [Fact]
    public async Task A_later_case_question_leaves_the_summary_before_it_as_the_latest()
    {
        var owner = new WhatsAppAuthoringChannelDouble();
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IStructuredAuthoringProposalChannel>(owner))));
        var contact = await host.ContactAsync(Ct, automatic: true);
        var summary = await SummaryAsync(host, contact.ContactId);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var chat = await db.Set<WhatsAppContact>().SingleAsync(row => row.Id == contact.ContactId, Ct);
            var now = host.Clock.GetUtcNow().UtcDateTime;
            var source = WhatsAppMessage.Inbound(contact.ContactId, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, "¿Puedo anotarme?", null, now);
            var requestId = Guid.NewGuid();
            var question = WhatsAppAssistantRequest.Create(requestId, contact.ContactId, source.Id, contact.RecipientId, contact.RecipientId,
                WhatsAppAssistantRequestKind.Automatic, chat.ModeRevision, chat.LinkRevision, "{\"channelId\":\"fixture\"}", now, now.AddDays(30));
            // As WhatsAppAssistantReplyPolicy queues it: the buttons of its own reference and the why of the agent's reply.
            var outgoing = WhatsAppOutgoingMessage.CreateAssistantQuickReplies(contact.RecipientId, contact.ContactId, requestId, "¿Registrar la visita?",
                [new(WhatsAppAssistantCaseButtonCodec.Encode(requestId, 0), "Sí", 0)], now, chat.LinkRevision, chat.WaId!, chat.ModeRevision);
            outgoing.ExplainAssistantReply("{\"kind\":\"askButtons\"}");
            Assert.True(question.Ready(Guid.NewGuid(), null, now));
            Assert.True(question.Accept(outgoing.Id, new string('a', 64), null, now));
            db.AddRange(source, question, outgoing);
            await db.SaveChangesAsync(Ct);
        });

        Assert.Equal(summary.RequestId, await host.ScopeAsync(services =>
            services.GetRequiredService<IWhatsAppAssistantReader>().FindLatestAuthoringRequestAsync(contact.ContactId, Ct)));
    }

    // Task 9 of the documents: a document question is also the own outgoing of an automatic reference with buttons, and without the
    // agent's why, but it is no summary either: the summary before it keeps its buttons.
    [Fact]
    public async Task A_later_document_question_leaves_the_summary_before_it_as_the_latest()
    {
        var owner = new WhatsAppAuthoringChannelDouble();
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IStructuredAuthoringProposalChannel>(owner))));
        var contact = await host.ContactAsync(Ct, automatic: true);
        var summary = await SummaryAsync(host, contact.ContactId);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var chat = await db.Set<WhatsAppContact>().SingleAsync(row => row.Id == contact.ContactId, Ct);
            var now = host.Clock.GetUtcNow().UtcDateTime;
            var document = WhatsAppMessage.Inbound(contact.ContactId, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ReceivedDocument, "Cargá esto", null, now);
            var requestId = Guid.NewGuid();
            var proposalId = Guid.NewGuid();
            var question = WhatsAppAssistantRequest.Create(requestId, contact.ContactId, document.Id, contact.RecipientId, contact.RecipientId,
                WhatsAppAssistantRequestKind.Automatic, chat.ModeRevision, chat.LinkRevision, "{\"channelId\":\"fixture\"}", now, now.AddDays(30));
            Assert.True(question.CarryAttachment(document.Id));
            // As WhatsAppAssistantDocumentPolicy asks it: the forms of its own proposal as buttons, without the agent's why.
            var outgoing = WhatsAppOutgoingMessage.CreateAssistantQuickReplies(contact.RecipientId, contact.ContactId, requestId, "¿Qué es este documento?",
                [new(WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposalId, Guid.NewGuid()), "Pagos", 0),
                    new(WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposalId, Guid.NewGuid()), "Gastos", 0)],
                now, chat.LinkRevision, chat.WaId!, chat.ModeRevision);
            Assert.True(question.Ready(proposalId, null, now));
            Assert.True(question.Accept(outgoing.Id, new string('a', 64), null, now));
            db.AddRange(document, question, outgoing);
            await db.SaveChangesAsync(Ct);
        });

        Assert.Equal(summary.RequestId, await host.ScopeAsync(services =>
            services.GetRequiredService<IWhatsAppAssistantReader>().FindLatestAuthoringRequestAsync(contact.ContactId, Ct)));
    }

    // «Pregunta y espera»: the question is a fixed text, not an offer nor a summary, and the answer continues the same operation.
    [Fact]
    public async Task An_unclear_request_asks_what_to_build_and_the_answer_continues_its_operation()
    {
        var owner = new WhatsAppAuthoringChannelDouble();
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IStructuredAuthoringProposalChannel>(owner))));
        var contact = await host.ContactAsync(Ct, automatic: true);

        var asked = await SummaryAsync(host, contact.ContactId, WhatsAppAuthoringChannelDouble.DeclineId);
        var question = await ReferenceAsync(host, asked.RequestId);
        Assert.Equal((WhatsAppAssistantRequestState.Sent, (Guid?)asked.RequestId), (question.State, question.ChangeRequestId));
        var text = await OutgoingAsync(host, question.OutgoingId!.Value);
        Assert.Equal((WhatsAppOutgoingMessageKind.Text,
            "No me queda claro qué querés que arme. Contame si es un formulario o un catálogo, para qué es y qué datos lleva, por escrito o con un audio."),
            (text.Kind, text.Body));
        var day = host.Clock.GetUtcNow().UtcDateTime.Date;
        var reads = await host.ScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IWhatsAppAssistantReader>();
            return (Offers: await reader.CountOffersAsync(contact.ContactId, day, day.AddDays(1), Ct), Continues: await reader.FindChangeRequestAsync(contact.ContactId, Ct),
                Latest: await reader.FindLatestAuthoringRequestAsync(contact.ContactId, Ct));
        });
        Assert.Equal((0, (Guid?)asked.RequestId, (Guid?)null), reads);

        await host.AddNewerSourceAsync(contact, Ct);
        var answered = await SummaryAsync(host, contact.ContactId);
        Assert.Equal(asked.RequestId, answered.Operation!.PreviousRequestId);
        var summary = await ReferenceAsync(host, answered.RequestId);
        Assert.Equal(((Guid?)asked.RequestId, (Guid?)null), (summary.PreviousRequestId, summary.ChangeRequestId));
        Assert.Equal(WhatsAppOutgoingMessageKind.QuickReplies, (await OutgoingAsync(host, summary.OutgoingId!.Value)).Kind);
        Assert.Null(await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantReader>().FindChangeRequestAsync(contact.ContactId, Ct)));
    }

    // Stage 3: the automatic conversation proposes version 2 of a form, which closes the operation with the same summary and buttons.
    [Fact]
    public async Task Version_two_of_a_form_sends_the_same_summary_with_its_buttons()
    {
        var owner = new WhatsAppAuthoringChannelDouble();
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IStructuredAuthoringProposalChannel>(owner))));
        var contact = await host.ContactAsync(Ct, automatic: true);
        var summary = await ReferenceAsync(host, (await SummaryAsync(host, contact.ContactId, version: 2)).RequestId);
        Assert.Equal(WhatsAppAssistantRequestState.Sent, summary.State);
        var buttons = await OutgoingAsync(host, summary.OutgoingId!.Value);
        Assert.StartsWith("Te propongo este formulario:\n*Parte de lluvias · LLU*", buttons.Body, StringComparison.Ordinal);
        Assert.Equal(["Crear borrador", "Cambiar algo"], buttons.QuickReplies.Select(reply => reply.Title));
    }

    // H20 with PostgreSQL: a tap on «Crear» after the number was unlinked creates nothing and is only marked.
    [Fact]
    public async Task Creating_after_the_number_was_unlinked_creates_nothing()
    {
        var owner = new WhatsAppAuthoringChannelDouble();
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IStructuredAuthoringProposalChannel>(owner))));
        var contact = await host.ContactAsync(Ct, automatic: true);
        var summary = await ReferenceAsync(host, (await SummaryAsync(host, contact.ContactId)).RequestId);
        var buttons = await OutgoingAsync(host, summary.OutgoingId!.Value);
        await host.InTransactionAsync(async services =>
        {
            (await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(item => item.Id == contact.ContactId, Ct)).UnlinkUser();
            return true;
        }, Ct);

        var create = await TapAsync(host, contact.ContactId, buttons.QuickReplies[0].Id);
        await ProcessTapAsync(host, contact.ContactId);

        Assert.Empty(owner.Created);
        await host.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.Set<WhatsAppAssistantRequest>().AsNoTracking().AnyAsync(row => row.ContactId == contact.ContactId && row.SourceMessageId == create, Ct));
            Assert.NotNull(await db.Set<WhatsAppMessage>().AsNoTracking().Where(message => message.Id == create).Select(message => message.AssistantProcessedAtUtc).SingleAsync(Ct));
            return true;
        });
    }

    private static async Task<WhatsAppAssistantWorkResponse> SummaryAsync(WhatsAppAssistantScenario host, Guid contactId,
        string capability = WhatsAppAuthoringChannelDouble.ProposeFormId, int version = 1)
    {
        var work = await host.ScopeAsync(async services => (await services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contactId, Ct)).Value);
        Assert.NotNull(work);
        var operation = Assert.IsType<AssistantOperationRequest>(work.Operation);
        var outcome = new AssistantOperationResponse(work.RequestId, "completed",
            new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), new(capability, version), new AuthoringPayload()),
            new("http-fixture", 1, "http-fixture", 1, operation.OccurredAtUtc, operation.Context.ChannelId), null);
        host.Operations.Complete(outcome);
        await host.CompleteOutcomeAsync(contactId, outcome, Ct);
        return work;
    }

    private static Task<WhatsAppAssistantRequest> ReferenceAsync(WhatsAppAssistantScenario host, Guid requestId) => host.ScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking().SingleAsync(row => row.RequestId == requestId, Ct));

    private static Task<WhatsAppOutgoingMessage> OutgoingAsync(WhatsAppAssistantScenario host, Guid outgoingId) => host.ScopeAsync(services =>
        services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking().SingleAsync(row => row.Id == outgoingId, Ct));

    // The reply to a tap is the outgoing of the reference whose source is that tap.
    private static async Task<WhatsAppOutgoingMessage> ReplyAsync(WhatsAppAssistantScenario host, Guid contactId, Guid tapId)
    {
        var reference = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppAssistantRequest>().AsNoTracking()
            .SingleAsync(row => row.ContactId == contactId && row.SourceMessageId == tapId, Ct));
        Assert.Equal(WhatsAppAssistantRequestState.Sent, reference.State);
        var outgoing = await OutgoingAsync(host, reference.OutgoingId!.Value);
        Assert.Equal(WhatsAppOutgoingMessageKind.Text, outgoing.Kind);
        return outgoing;
    }

    private static Task<IReadOnlyList<Guid>> PendingAsync(WhatsAppAssistantScenario host) => host.ScopeAsync(services =>
        services.GetRequiredService<IWhatsAppAssistantReader>().ListPendingContactsAsync([], 100, host.Clock.GetUtcNow().UtcDateTime, Ct));

    private static async Task ProcessTapAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        Assert.True((await host.ScopeAsync(services => services.GetRequiredService<IWhatsAppAssistantAuthoringService>().ProcessTapAsync(contactId, Ct))).IsSuccess);

    private static Task<Guid> TapAsync(WhatsAppAssistantScenario host, Guid contactId, string replyId) => host.InTransactionAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, Ct);
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ButtonReply, "Botón", replyId, now);
        message.Classify(WhatsAppMessagePurpose.Business); message.SetInboundSequence(contact.RegisterInboundMessage()); message.MarkProcessed(now);
        contact.RecordInbound(contact.WaId, contact.UserIdentifier, contact.ProfileName, now); db.Add(message);
        Assert.True(WhatsAppAssistantAuthoringReplyCodec.TryParse(replyId, out _, out _, out _));
        return message.Id;
    }, Ct);
}
