using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FakeFormWorkbookWriter : IFormWorkbookWriter
{
    public FormWorkbookRequest? Request { get; private set; }
    public Action? BeforeReturn { get; set; }
    public byte[] Write(FormWorkbookRequest request, CancellationToken cancellationToken) { Request = request; BeforeReturn?.Invoke(); return [1]; }
}
