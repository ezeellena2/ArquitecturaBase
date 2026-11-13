using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// Task 9c of the documents: what a capture through WhatsApp said and what its reply carried (the answers as the capture decoded them, the
/// sealed projection, payload and content of its send), for what its rejection answers in the unit of work that records it.
/// </summary>
public sealed record WhatsAppFlowRejection(StructuredRecordCaptureResponse Capture, IReadOnlyDictionary<string, string> Answers,
    WhatsAppFlowProjection Projection, WhatsAppFlowSessionPayload Payload, WhatsAppFlowAssistantContent? Content)
{
    public override string ToString() => "WhatsAppFlowRejection { Content = [REDACTED] }";
}

/// <summary>Task 9c: what a rejected capture answers: nothing, the form again after a fixed validation, or the notice of a repeated one.</summary>
public enum WhatsAppFlowRejectionAnswer { None, Resend, Notice }
