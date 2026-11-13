namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;

/// <summary>`BusinessForms:DocumentRereadWorker`: whether the documents of a form built from them are read again in the background and how often it looks.</summary>
public sealed class FormDocumentRereadWorkerOptions
{
    public bool ProcessInBackground { get; set; } = true;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
}
