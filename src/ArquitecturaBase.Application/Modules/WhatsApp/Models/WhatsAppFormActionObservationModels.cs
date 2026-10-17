using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;
public sealed record WhatsAppFormActionObservationRow(Guid RunId,Guid RecipientUserId,WhatsAppOutgoingMessageState State,
    string? FailureCode,bool HasBody,long LastFormActionControlRevision,bool? DeliveryReviewWasSent,Guid? DeliveryReviewedByUserId,
    DateTime? DeliveryReviewedAtUtc,DateTime? DeliveredAtUtc,DateTime? ReadAtUtc)
{ public override string ToString()=>"WhatsAppFormActionObservationRow { Target = [REDACTED] }"; }
