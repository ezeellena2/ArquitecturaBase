namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>Neighbors and one-based position in the complete filtered and ordered record set.</summary>
public sealed record FormRecordNavigationResponse(Guid RecordId, Guid? PreviousId, Guid? NextId, int Position, int TotalCount);
public sealed record FormRecordNavigationRow(Guid RecordId, Guid? PreviousId, Guid? NextId, int Position, int TotalCount);
