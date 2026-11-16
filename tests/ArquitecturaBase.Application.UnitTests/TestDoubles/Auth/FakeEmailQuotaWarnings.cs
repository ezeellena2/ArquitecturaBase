using ArquitecturaBase.Application.Interfaces.Integrations.Emails;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

/// <summary>
/// Los avisos del tope diario del correo (N7) sin la memoria de la copia: anota qué aviso se pidió y deja pasar según
/// <see cref="Allows"/>. Que salga uno por día lo prueba la implementación (EmailQuotaWarningsTests, en la integración).
/// </summary>
internal sealed class FakeEmailQuotaWarnings : IEmailQuotaWarnings
{
    public bool Allows { get; set; } = true;

    /// <summary>"near" o "reached", en el orden en que se pidieron.</summary>
    public List<string> Asked { get; } = [];

    public bool TryWarnNear(DateTime nowUtc)
    {
        Asked.Add("near");
        return Allows;
    }

    public bool TryWarnReached(DateTime nowUtc)
    {
        Asked.Add("reached");
        return Allows;
    }
}
