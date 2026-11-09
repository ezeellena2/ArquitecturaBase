namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>Lo que decide una persona sobre una emisión en revisión.</summary>
public enum ArcaVoucherReviewDecision
{
    /// <summary>Para un número tomado o que falta: emitir con el próximo número libre.</summary>
    IssueWithNextNumber,

    /// <summary>Para la fecha fuera de la ventana: emitir con la fecha de hoy.</summary>
    IssueToday,

    /// <summary>No emitir.</summary>
    DoNotIssue,
}
