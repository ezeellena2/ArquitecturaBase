namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;

/// <summary>`BusinessForms:CatalogFeedWorker`: whether the fed catalogs refresh in the background and how often it looks.</summary>
public sealed class FormCatalogFeedWorkerOptions
{
    public bool ProcessInBackground { get; set; } = true;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(5);
}
