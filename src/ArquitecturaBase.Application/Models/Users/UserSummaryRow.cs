namespace ArquitecturaBase.Application.Models.Users;

/// <summary>Complete counts of nondeleted accounts, without personal information.</summary>
public sealed record UserSummaryRow(int ActiveCount, int InactiveCount);
