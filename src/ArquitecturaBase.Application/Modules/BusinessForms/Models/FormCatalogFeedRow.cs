namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>A fed catalog: its key, when its source last answered or failed, and until when a host holds its refresh.</summary>
public sealed record FormCatalogFeedRow(string Key, DateTime? RefreshedAtUtc, DateTime? FailedAtUtc, DateTime? LeaseUntilUtc);
