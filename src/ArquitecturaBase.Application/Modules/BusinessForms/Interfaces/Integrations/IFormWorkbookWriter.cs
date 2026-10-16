using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
public interface IFormWorkbookWriter
{
    byte[] Write(FormWorkbookRequest request, CancellationToken cancellationToken);
}
