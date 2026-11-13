using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// The dispatcher calls the owner outside its own UoW, then deduplicates the fixed reply in its existing outbox. An authoring
// proposal (a form, a catalog, an import, H4's people catalog or why nothing can be built) is only summarized here: its button
// creates it, or opens the import in the app. A read document's proposal (task 9 of the documents) goes to the inbox, which says
// what to tell the person.
internal sealed class WhatsAppAssistantTerminalPolicy(IStructuredAuthoringProposalChannel authoring,
    IStructuredMissingFormRequestSink missing, IPublicOrigin origin, IWhatsAppAssistantReader reader, WhatsAppAssistantDocumentPolicy documents,
    WhatsAppAssistantSpreadsheetPolicy spreadsheets, WhatsAppAssistantLoadAcceptancePolicy loads)
{
    private static readonly string[] Ids = ["forms.missing-request.propose", "forms.catalog-filters.propose", "forms.authoring.propose-form",
        "forms.authoring.propose-catalog", "forms.authoring.propose-import", "forms.authoring.decline"];

    // Version 2 of a catalog maps the spreadsheet (IA, Stage 2) and version 2 of a form reads the conversation's document (Stage 3);
    // version 1 still closes what is in progress.
    public static bool Handles(AssistantCapabilityReference capability) => capability.Version == 1 && Ids.Contains(capability.Id, StringComparer.Ordinal)
        || capability is { Id: "forms.authoring.propose-catalog" or "forms.authoring.propose-form", Version: 2 }
        || WhatsAppAssistantDocumentPolicy.Handles(capability) || WhatsAppAssistantSpreadsheetPolicy.Handles(capability);

    /// <summary>
    /// Task 9: an interpretation that only loads a read document and closed without a proposal still goes to the inbox, which leaves it
    /// unclassified and offers what the person can complete. VC1-A (task 29): so does one that only loads a spreadsheet.
    /// </summary>
    public static bool HandlesDocumentWithoutProposal(WhatsAppAssistantReferenceRow reference, AssistantOperationResponse operation) =>
        operation is { State: "completed", Proposal: null } && reference.AttachmentMessageId is not null
        && reference.Kind == WhatsAppAssistantRequestKind.Automatic && WhatsAppAssistantSuggestionPolicy.Context(reference) is var context
        && (WhatsAppAssistantAccessPolicy.LoadsDocumentOnly(context) || WhatsAppAssistantAccessPolicy.LoadsSpreadsheetOnly(context));

    /// <summary>VC1-A (task 29): whether an outcome registers the rows of a spreadsheet: their proposal, or a spreadsheet alone without one.</summary>
    public static bool RegistersRows(WhatsAppAssistantReferenceRow reference, AssistantOperationResponse operation) =>
        operation.Proposal is { } proposal ? WhatsAppAssistantSpreadsheetPolicy.Handles(proposal.Capability)
            : HandlesDocumentWithoutProposal(reference, operation) && WhatsAppAssistantAccessPolicy.LoadsSpreadsheetOnly(WhatsAppAssistantSuggestionPolicy.Context(reference));

    public async Task<Result<WhatsAppAssistantTerminalResponse?>> ApplyAsync(WhatsAppAssistantReferenceRow reference,
        WhatsAppAssistantSourceRow source, AssistantOperationResponse operation, CancellationToken ct)
    {
        var documentOnly = HandlesDocumentWithoutProposal(reference, operation);
        if (!documentOnly && (operation.State != "completed" || operation.Proposal is not {} handled || operation.Provenance is null || !Handles(handled.Capability)))
            return Result.Success<WhatsAppAssistantTerminalResponse?>(null);
        if (operation.RequestId != reference.RequestId || source.Id != reference.SourceMessageId || source.ContactId != reference.ContactId)
            return WhatsAppAssistantErrors.InvalidProposal;
        var context = WhatsAppAssistantSuggestionPolicy.Context(reference);
        if (context.ActorUserId != reference.ActorUserId || context.SubjectUserId != reference.SubjectUserId
            || context.ChannelId != (reference.Kind == WhatsAppAssistantRequestKind.Automatic ? "whatsapp-assistant" : "whatsapp-manual")
            || context.InteractionMode != (reference.Kind == WhatsAppAssistantRequestKind.Automatic ? AssistantInteractionMode.Assistant : AssistantInteractionMode.Manual)
            || reference.Kind == WhatsAppAssistantRequestKind.Automatic && reference.ActorUserId != reference.SubjectUserId)
            return WhatsAppAssistantErrors.InvalidProposal;
        var automatic = reference.Kind == WhatsAppAssistantRequestKind.Automatic;
        // VC1-A (task 29): the rows of a spreadsheet go to the inbox in the person's own conversation, which says what became of each; a
        // spreadsheet alone that closed without a proposal too, which the inbox leaves unclassified with the forms to choose.
        if (RegistersRows(reference, operation))
            return automatic ? await spreadsheets.RegisterAsync(reference, source, operation, context, ct)
                : new WhatsAppAssistantTerminalResponse(operation.Proposal?.Id ?? reference.RequestId, null, context.Culture);
        // A read document goes to the inbox in the person's own conversation, which says what to answer.
        if (documentOnly || WhatsAppAssistantDocumentPolicy.Handles(operation.Proposal!.Capability))
            return automatic ? await documents.RegisterAsync(reference, operation, context, ct) : new WhatsAppAssistantTerminalResponse(operation.Proposal?.Id ?? reference.RequestId, null, context.Culture);
        var proposal = operation.Proposal!;
        var provenance = operation.Provenance!;
        if (proposal.Capability.Id != "forms.missing-request.propose")
        {
            if (!automatic) return new WhatsAppAssistantTerminalResponse(proposal.Id, null, context.Culture);
            var summary = authoring.Summarize(reference.RequestId, proposal, context.Culture);
            // Asking what to build with a spreadsheet in the conversation asks what to do with it.
            var spreadsheet = summary is { Kind: StructuredAuthoringKind.Declined, Reason: "unclear" } && await HasSpreadsheetAsync(reference, ct);
            var reply = summary is null ? null
                : WhatsAppAssistantAuthoringText.Summary(reference.RequestId, proposal.Id, summary, context.Culture, origin.Value, spreadsheet);
            return reply is null ? WhatsAppAssistantErrors.Unavailable : reply;
        }
        var captured = await missing.CaptureAsync(new StructuredMissingFormRequestCaptureRequest(source.Id, source.OccurredAtUtc, context, proposal, provenance), ct);
        if (captured.IsFailure) return captured.Error;
        return new WhatsAppAssistantTerminalResponse(proposal.Id, automatic ? AssistantTerminalTexts.Get("MissingFormRequested", context.Culture) : null, context.Culture);
    }

    /// <summary>VC1-A (task 29): a spreadsheet to load over the day's quota, outside any transaction.</summary>
    public Task<Result<WhatsAppAssistantTerminalResponse?>> RegisterSpreadsheetOverQuotaAsync(WhatsAppAssistantReferenceRow reference,
        WhatsAppAssistantSourceRow source, CancellationToken ct) => spreadsheets.RegisterOverQuotaAsync(reference, source, ct);

    /// <summary>VC1-A (task 29): the notice of a spreadsheet and its loads, in the caller's transaction.</summary>
    public Task<Result> AcceptLoadsAsync(WhatsAppContact contact, WhatsAppAssistantRequest row, WhatsAppAssistantTerminalResponse terminal, CancellationToken ct) =>
        loads.AcceptAsync(contact, row, terminal, ct);

    // The conversation has a spreadsheet when this request carries one or an operation it continues did, as the engine inherits it:
    // the references of a conversation chain by the operation each one continues.
    private async Task<bool> HasSpreadsheetAsync(WhatsAppAssistantReferenceRow reference, CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        for (var current = reference; current is not null;
             current = current.PreviousRequestId is { } previous && seen.Add(previous) ? await reader.FindAsync(reference.ContactId, previous, ct) : null)
            // The latest file decides, as the engine inherits it: a read document (task 9) is not a spreadsheet.
            if (current.AttachmentMessageId is { } attachment) return await reader.FindDocumentAsync(reference.ContactId, attachment, ct) is null;
        return false;
    }
}
